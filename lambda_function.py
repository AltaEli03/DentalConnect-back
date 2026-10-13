"""DentalConnect public clinic API and Cognito-backed patient verification."""
import hashlib
import hmac
import html
import json
import os
import re
import secrets
import smtplib
import ssl
import time
from decimal import Decimal
from email.message import EmailMessage

import boto3
from botocore.exceptions import ClientError

TABLE = os.environ.get("CLINICS_TABLE", "DentalConnectClinics")
table = boto3.resource("dynamodb").Table(TABLE)
cognito = boto3.client("cognito-idp")
POOL_ID = os.environ.get("COGNITO_USER_POOL_ID", "us-east-1_hVMMVyRtS")
FRONTEND_ORIGIN = os.environ.get("FRONTEND_ORIGIN", "https://main.d63zai2tg2i2u.amplifyapp.com")
CODE_TTL_SECONDS = 600
RESEND_SECONDS = 60
MAX_ATTEMPTS = 5


class Encoder(json.JSONEncoder):
    def default(self, value):
        if isinstance(value, Decimal):
            return float(value)
        return super().default(value)


def response(status, payload=None, origin=FRONTEND_ORIGIN):
    headers = {
        "Content-Type": "application/json",
        "Access-Control-Allow-Origin": origin,
        "Access-Control-Allow-Headers": "Content-Type",
        "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
        "Vary": "Origin",
    }
    return {"statusCode": status, "headers": headers, "body": "" if payload is None else json.dumps(payload, cls=Encoder)}


def _email_key(email):
    digest = hashlib.sha256(email.strip().lower().encode()).hexdigest()
    return f"AUTH#{digest}"


def _normalize_phone(phone):
    digits = re.sub(r"\D", "", phone)
    normalized = f"+{digits}" if phone.strip().startswith("+") else f"+{digits if digits.startswith('52') else '52' + digits}"
    return normalized if re.fullmatch(r"\+[1-9]\d{7,14}", normalized) else ""


def _code_hash(email, code):
    pepper = os.environ.get("AUTH_CODE_PEPPER", "")
    if len(pepper) < 32:
        raise RuntimeError("AUTH_CODE_PEPPER must be configured as a secret")
    return hmac.new(pepper.encode(), f"{email}:{code}".encode(), hashlib.sha256).hexdigest()


def _send_code(email, first_name, code):
    required = ("SMTP_HOST", "SMTP_USER", "SMTP_PASSWORD", "EMAIL_FROM")
    if any(not os.environ.get(name) for name in required):
        raise RuntimeError("El servicio de correo no está configurado")
    port = int(os.environ.get("SMTP_PORT", "587"))
    message = EmailMessage()
    message["Subject"] = "Verifica tu cuenta de DentalConnect"
    message["From"] = os.environ["EMAIL_FROM"]
    message["To"] = email
    message.set_content(f"Hola {first_name}, tu código de verificación es {code}. Expira en 10 minutos.")
    message.add_alternative(
        f"<p>Hola {html.escape(first_name)},</p><p>Tu código de verificación es <strong>{code}</strong>.</p><p>Expira en 10 minutos.</p>",
        subtype="html",
    )
    context = ssl.create_default_context()
    with smtplib.SMTP(os.environ["SMTP_HOST"], port, timeout=10) as server:
        server.starttls(context=context)
        server.login(os.environ["SMTP_USER"], os.environ["SMTP_PASSWORD"])
        server.send_message(message)


def _issue_code(email, first_name, previous=None):
    now = int(time.time())
    if previous and int(previous.get("resends", 0)) >= 5:
        return response(429, {"error": {"code": "RESEND_LIMIT", "message": "Se alcanzó el límite de reenvíos. Inténtalo más tarde."}})
    if previous and now - int(previous.get("lastSentAt", 0)) < RESEND_SECONDS:
        return response(429, {"error": {"code": "RESEND_TOO_SOON", "message": "Espera antes de solicitar otro código."}})
    code = f"{secrets.randbelow(1_000_000):06d}"
    record = {
        "id": _email_key(email),
        "entityType": "auth_verification",
        "codeHash": _code_hash(email, code),
        "expiresAt": now + CODE_TTL_SECONDS,
        "lastSentAt": now,
        "attempts": 0,
        "resends": int(previous.get("resends", 0) if previous else 0) + (1 if previous else 0),
    }
    _send_code(email, first_name, code)
    table.put_item(Item=record)
    return response(200, {"data": {"message": "Código enviado. Revisa tu correo."}})


def _body(event):
    raw = event.get("body") or "{}"
    if event.get("isBase64Encoded"):
        import base64
        raw = base64.b64decode(raw).decode()
    return json.loads(raw)


def _register(body):
    first = str(body.get("firstName", "")).strip()
    last = str(body.get("lastName", "")).strip()
    email = str(body.get("email", "")).strip().lower()
    phone = _normalize_phone(str(body.get("phone", "")).strip())
    password = str(body.get("password", ""))
    if not first or not last or not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", email) or not phone or len(password) < 12:
        return response(400, {"error": {"code": "INVALID_INPUT", "message": "Revisa nombre, apellidos, correo, teléfono y contraseña (mínimo 12 caracteres)."}})
    try:
        cognito.admin_create_user(
            UserPoolId=POOL_ID,
            Username=email,
            TemporaryPassword=password,
            MessageAction="SUPPRESS",
            UserAttributes=[
                {"Name": "email", "Value": email}, {"Name": "email_verified", "Value": "false"},
                {"Name": "given_name", "Value": first}, {"Name": "family_name", "Value": last},
                {"Name": "phone_number", "Value": phone},
            ],
        )
    except ClientError as exc:
        if exc.response.get("Error", {}).get("Code") == "UsernameExistsException":
            return response(409, {"error": {"code": "EMAIL_EXISTS", "message": "Ya existe una cuenta con ese correo."}})
        raise
    try:
        sent = _issue_code(email, first)
    except Exception:
        cognito.admin_delete_user(UserPoolId=POOL_ID, Username=email)
        raise
    if sent["statusCode"] >= 400:
        # Remove the unverified identity if a code could not be delivered.
        cognito.admin_delete_user(UserPoolId=POOL_ID, Username=email)
    return sent


def _verify(body):
    email = str(body.get("email", "")).strip().lower()
    code = str(body.get("code", "")).strip()
    password = str(body.get("password", ""))
    if not email or not re.fullmatch(r"\d{6}", code) or len(password) < 12:
        return response(400, {"error": {"code": "INVALID_CODE", "message": "Ingresa el correo y el código de seis dígitos."}})
    item = table.get_item(Key={"id": _email_key(email)}).get("Item")
    now = int(time.time())
    if not item or item.get("entityType") != "auth_verification" or int(item.get("expiresAt", 0)) <= now:
        return response(400, {"error": {"code": "CODE_EXPIRED", "message": "El código venció. Solicita uno nuevo."}})
    if int(item.get("attempts", 0)) >= MAX_ATTEMPTS:
        return response(429, {"error": {"code": "TOO_MANY_ATTEMPTS", "message": "Se alcanzó el límite. Solicita un nuevo código."}})
    if not hmac.compare_digest(item.get("codeHash", ""), _code_hash(email, code)):
        table.update_item(Key={"id": item["id"]}, UpdateExpression="SET attempts = attempts + :one", ExpressionAttributeValues={":one": 1})
        return response(400, {"error": {"code": "INVALID_CODE", "message": "El código no es válido."}})
    cognito.admin_update_user_attributes(UserPoolId=POOL_ID, Username=email, UserAttributes=[{"Name": "email_verified", "Value": "true"}])
    cognito.admin_set_user_password(UserPoolId=POOL_ID, Username=email, Password=password, Permanent=True)
    table.delete_item(Key={"id": item["id"]})
    return response(200, {"data": {"message": "Correo verificado. Ya puedes iniciar sesión."}})


def _resend(body):
    email = str(body.get("email", "")).strip().lower()
    if not email:
        return response(400, {"error": {"code": "INVALID_EMAIL", "message": "Ingresa un correo válido."}})
    item = table.get_item(Key={"id": _email_key(email)}).get("Item")
    if not item or item.get("entityType") != "auth_verification":
        return response(400, {"error": {"code": "NO_PENDING_VERIFICATION", "message": "No hay una verificación pendiente para ese correo."}})
    try:
        user = cognito.admin_get_user(UserPoolId=POOL_ID, Username=email)
    except ClientError as exc:
        if exc.response.get("Error", {}).get("Code") == "UserNotFoundException":
            table.delete_item(Key={"id": item["id"]})
            return response(400, {"error": {"code": "NO_PENDING_VERIFICATION", "message": "No hay una verificación pendiente para ese correo."}})
        raise
    attrs = {attr["Name"]: attr["Value"] for attr in user.get("UserAttributes", [])}
    return _issue_code(email, attrs.get("given_name", ""), item)


def lambda_handler(event, _context):
    origin = (event.get("headers") or {}).get("origin", FRONTEND_ORIGIN)
    allowed_origin = origin if origin == FRONTEND_ORIGIN else FRONTEND_ORIGIN
    if event.get("requestContext", {}).get("http", {}).get("method") == "OPTIONS":
        return response(204, origin=allowed_origin)
    path = (event.get("rawPath") or event.get("requestContext", {}).get("http", {}).get("path", "/")).rstrip("/") or "/"
    method = event.get("requestContext", {}).get("http", {}).get("method", "GET").upper()
    try:
        if path in ("/health", "/api/health"):
            return response(200, {"status": "ok", "service": "DentalConnect API"}, allowed_origin)
        if path in ("/api/auth/register", "/auth/register") and method == "POST":
            return _register(_body(event))
        if path in ("/api/auth/verify-email", "/auth/verify-email") and method == "POST":
            return _verify(_body(event))
        if path in ("/api/auth/resend-verification-code", "/auth/resend-verification-code") and method == "POST":
            return _resend(_body(event))
        if path in ("/api/clinics", "/clinics") and method == "GET":
            items = [item for item in table.scan().get("Items", []) if item.get("entityType") != "auth_verification"]
            search = (event.get("queryStringParameters") or {}).get("search", "").lower()
            if search:
                items = [item for item in items if search in item.get("name", "").lower() or any(search in service.get("name", "").lower() for service in item.get("services", []))]
            return response(200, {"data": items}, allowed_origin)
        if (path.startswith("/api/clinics/") or path.startswith("/clinics/")) and method == "GET":
            clinic_id = path.rsplit("/", 1)[-1]
            item = table.get_item(Key={"id": clinic_id}).get("Item")
            return response(200, {"data": item}, allowed_origin) if item and item.get("entityType") != "auth_verification" else response(404, {"error": {"code": "CLINIC_NOT_FOUND", "message": "Consultorio no encontrado"}}, allowed_origin)
        return response(404, {"error": {"code": "NOT_FOUND", "message": "Ruta no encontrada"}}, allowed_origin)
    except (ValueError, TypeError, json.JSONDecodeError):
        return response(400, {"error": {"code": "INVALID_REQUEST", "message": "La solicitud no es válida."}}, allowed_origin)
    except ClientError as exc:
        error_code = exc.response.get("Error", {}).get("Code", "")
        if error_code in ("InvalidPasswordException", "InvalidParameterException"):
            return response(400, {"error": {"code": "INVALID_ACCOUNT_DATA", "message": "La contraseña o los datos no cumplen la política de seguridad de la cuenta."}}, allowed_origin)
        if error_code in ("NotAuthorizedException", "CodeMismatchException"):
            return response(400, {"error": {"code": "INVALID_CODE", "message": "El código o la contraseña no son válidos."}}, allowed_origin)
        print(f"DentalConnect auth provider error: {error_code or 'ClientError'}")
        return response(503, {"error": {"code": "SERVICE_UNAVAILABLE", "message": "No fue posible completar la operación. Intenta más tarde."}}, allowed_origin)
    except Exception as exc:
        # Never log request bodies, credentials, verification codes or provider responses.
        print(f"DentalConnect API error: {type(exc).__name__}")
        return response(503, {"error": {"code": "SERVICE_UNAVAILABLE", "message": "No fue posible completar la operación. Intenta más tarde."}}, allowed_origin)
