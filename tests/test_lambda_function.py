import hashlib
import hmac
import importlib.util
import json
import os
import sys
import types
import unittest
from unittest.mock import MagicMock


# Lambda includes boto3; provide small test doubles so CI needs no AWS credentials.
fake_table = MagicMock()
fake_cognito = MagicMock()
fake_boto3 = types.ModuleType("boto3")
fake_boto3.resource = MagicMock(return_value=MagicMock(Table=MagicMock(return_value=fake_table)))
fake_boto3.client = MagicMock(return_value=fake_cognito)
fake_botocore = types.ModuleType("botocore")
fake_botocore_exceptions = types.ModuleType("botocore.exceptions")


class ClientError(Exception):
    def __init__(self, response, operation_name):
        super().__init__(operation_name)
        self.response = response


fake_botocore_exceptions.ClientError = ClientError
sys.modules.setdefault("boto3", fake_boto3)
sys.modules.setdefault("botocore", fake_botocore)
sys.modules.setdefault("botocore.exceptions", fake_botocore_exceptions)
spec = importlib.util.spec_from_file_location("lambda_function_under_test", "lambda_function.py")
api = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api)


class LambdaAuthTests(unittest.TestCase):
    def setUp(self):
        fake_table.reset_mock()
        fake_cognito.reset_mock()
        os.environ["AUTH_CODE_PEPPER"] = "p" * 48

    def invoke(self, path, body=None, method="POST"):
        return api.lambda_handler({
            "rawPath": path,
            "requestContext": {"http": {"method": method}},
            "headers": {"origin": api.FRONTEND_ORIGIN},
            "body": json.dumps(body or {}),
        }, None)

    def test_registration_suppresses_cognito_email_and_sends_six_digit_code(self):
        api._send_code = MagicMock()
        api.secrets.randbelow = MagicMock(return_value=7)
        result = self.invoke("/api/auth/register", {
            "firstName": "Ana", "lastName": "Pérez", "email": "ANA@example.com",
            "phone": "+522221234567", "password": "DentConnect123!",
        })
        self.assertEqual(result["statusCode"], 200)
        self.assertEqual(fake_cognito.admin_create_user.call_args.kwargs["MessageAction"], "SUPPRESS")
        api._send_code.assert_called_once_with("ana@example.com", "Ana", "000007")
        stored = fake_table.put_item.call_args.kwargs["Item"]
        self.assertEqual(stored["entityType"], "auth_verification")
        self.assertNotIn("000007", stored["codeHash"])
        self.assertNotIn("password", stored)
        self.assertNotIn("000007", result["body"])

    def test_valid_code_verifies_cognito_user_and_activates_password(self):
        email, code = "ana@example.com", "123456"
        stored_id = api._email_key(email)
        stored = {"id": stored_id, "entityType": "auth_verification", "codeHash": api._code_hash(email, code), "expiresAt": 9999999999, "attempts": 0}
        fake_table.get_item.return_value = {"Item": stored}
        result = self.invoke("/api/auth/verify-email", {"email": email, "code": code, "password": "DentConnect123!"})
        self.assertEqual(result["statusCode"], 200)
        fake_cognito.admin_update_user_attributes.assert_called_once()
        fake_cognito.admin_set_user_password.assert_called_once_with(UserPoolId=api.POOL_ID, Username=email, Password="DentConnect123!", Permanent=True)
        fake_table.delete_item.assert_called_once_with(Key={"id": stored_id})

    def test_invalid_code_increments_attempts_and_never_confirms_user(self):
        email = "ana@example.com"
        fake_table.get_item.return_value = {"Item": {"id": api._email_key(email), "entityType": "auth_verification", "codeHash": "0" * 64, "expiresAt": 9999999999, "attempts": 0}}
        result = self.invoke("/api/auth/verify-email", {"email": email, "code": "123456", "password": "DentConnect123!"})
        self.assertEqual(result["statusCode"], 400)
        fake_table.update_item.assert_called_once()
        fake_cognito.admin_set_user_password.assert_not_called()

    def test_clinic_listing_hides_authentication_records(self):
        fake_table.scan.return_value = {"Items": [{"id": "clinic-1", "name": "Clínica Uno"}, {"id": "AUTH#hidden", "entityType": "auth_verification"}]}
        result = self.invoke("/api/clinics", method="GET")
        self.assertEqual(result["statusCode"], 200)
        self.assertEqual(json.loads(result["body"])["data"], [{"id": "clinic-1", "name": "Clínica Uno"}])


if __name__ == "__main__":
    unittest.main()
