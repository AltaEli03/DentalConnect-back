import 'dotenv/config';
import { z } from 'zod';

const environmentSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),
  DATABASE_URL: z.string().url().startsWith('postgresql://').optional(),
  FRONTEND_ORIGIN: z.string().url().default('http://localhost:5173'),
  FRONTEND_URL: z.string().url().default('http://localhost:5173'),
  JWT_SECRET: z.string().min(32).default('development-only-jwt-secret-change-this-now'),
  JWT_EXPIRES_IN: z.string().default('1h'),
  COOKIE_SECURE: z.enum(['true', 'false']).default('false').transform((value) => value === 'true'),
  COOKIE_SAME_SITE: z.enum(['lax', 'strict', 'none']).default('lax'),
  EMAIL_PROVIDER: z.enum(['console', 'smtp']).default('console'),
  EMAIL_FROM: z.string().email().default('no-reply@dentalconnect.example'),
  SMTP_HOST: z.string().optional(),
  SMTP_PORT: z.coerce.number().int().positive().optional(),
  SMTP_USER: z.string().optional(),
  SMTP_PASSWORD: z.string().optional(),
  AWS_REGION: z.string().optional(),
});

const parsedEnvironment = environmentSchema.parse(process.env);

if (parsedEnvironment.NODE_ENV === 'production') {
  if (parsedEnvironment.JWT_SECRET === 'development-only-jwt-secret-change-this-now') {
    throw new Error('JWT_SECRET debe configurarse con un valor único en producción');
  }
  if (!parsedEnvironment.COOKIE_SECURE) {
    throw new Error('COOKIE_SECURE debe ser true en producción');
  }
  if (parsedEnvironment.EMAIL_PROVIDER === 'console') {
    throw new Error('EMAIL_PROVIDER debe ser smtp en producción');
  }
  if (
    !parsedEnvironment.SMTP_HOST ||
    !parsedEnvironment.SMTP_PORT ||
    !parsedEnvironment.SMTP_USER ||
    !parsedEnvironment.SMTP_PASSWORD
  ) {
    throw new Error('SMTP_HOST, SMTP_PORT, SMTP_USER y SMTP_PASSWORD deben configurarse en producción');
  }
}

if (parsedEnvironment.COOKIE_SAME_SITE === 'none' && !parsedEnvironment.COOKIE_SECURE) {
  throw new Error('COOKIE_SECURE debe ser true cuando COOKIE_SAME_SITE=none');
}

export const env = parsedEnvironment;
