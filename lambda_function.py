"""Free-tier Lambda adapter for the public DentalConnect directory API."""
import json
import os
from decimal import Decimal

import boto3

TABLE = os.environ.get("CLINICS_TABLE", "DentalConnectClinics")
table = boto3.resource("dynamodb").Table(TABLE)


class Encoder(json.JSONEncoder):
    def default(self, value):
        if isinstance(value, Decimal):
            return float(value)
        return super().default(value)


def response(status, payload):
    return {"statusCode": status, "headers": {"Content-Type": "application/json", "Access-Control-Allow-Origin": "*"}, "body": json.dumps(payload, cls=Encoder)}


def lambda_handler(event, _context):
    path = event.get("rawPath") or event.get("requestContext", {}).get("http", {}).get("path", "/")
    if path in ("/health", "/api/health"):
        return response(200, {"status": "ok", "service": "DentalConnect API"})
    if path in ("/api/clinics", "/clinics"):
        items = table.scan().get("Items", [])
        search = (event.get("queryStringParameters") or {}).get("search", "").lower()
        if search:
            items = [item for item in items if search in item["name"].lower() or any(search in service["name"].lower() for service in item.get("services", []))]
        return response(200, {"data": items})
    if path.startswith("/api/clinics/") or path.startswith("/clinics/"):
        clinic_id = path.rsplit("/", 1)[-1]
        item = table.get_item(Key={"id": clinic_id}).get("Item")
        return response(200, {"data": item}) if item else response(404, {"error": {"code": "CLINIC_NOT_FOUND", "message": "Consultorio no encontrado"}})
    return response(404, {"error": {"code": "NOT_FOUND", "message": "Ruta no encontrada"}})

