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
  COOKIE_SECURE: z.coerce.boolean().default(false),
  COOKIE_SAME_SITE: z.enum(['lax', 'strict', 'none']).default('lax'),
  EMAIL_PROVIDER: z.enum(['console', 'smtp', 'ses']).default('console'),
  EMAIL_FROM: z.string().email().default('no-reply@dentalconnect.example'),
  SMTP_HOST: z.string().optional(),
  SMTP_PORT: z.coerce.number().int().positive().optional(),
  SMTP_USER: z.string().optional(),
  SMTP_PASSWORD: z.string().optional(),
  AWS_REGION: z.string().optional(),
});

export const env = environmentSchema.parse(process.env);
