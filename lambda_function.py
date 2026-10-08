"""Small public API used to demonstrate continuous deployment."""
import json


def response(status_code, body):
    return {
        "statusCode": status_code,
        "headers": {
            "Content-Type": "application/json; charset=utf-8",
            "Access-Control-Allow-Origin": "*",
        },
        "body": json.dumps(body, ensure_ascii=False),
    }


def lambda_handler(event, context):
    path = event.get("rawPath") or event.get("path") or "/"
    if path == "/health":
        return response(200, {"status": "ok", "service": "DentalConnect API", "version": "1.0"})
    if path == "/":
        return response(200, {
            "message": "DentalConnect API disponible",
            "health": "/health",
            "version": "1.0",
        })
    return response(404, {"message": "Ruta no encontrada"})
