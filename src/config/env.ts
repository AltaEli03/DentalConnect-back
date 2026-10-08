import 'dotenv/config';
import { z } from 'zod';

const environmentSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),
  DATABASE_URL: z.string().url().startsWith('postgresql://').optional(),
  FRONTEND_ORIGIN: z.string().url().default('http://localhost:5173'),
});

export const env = environmentSchema.parse(process.env);
