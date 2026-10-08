import cors from 'cors';
import express, { type NextFunction, type Request, type Response } from 'express';
import { ZodError, z } from 'zod';
import { env } from './config/env.js';
import type { ClinicRepository } from './types/clinic.js';

const clinicIdSchema = z.object({ id: z.string().cuid() });
const clinicsQuerySchema = z.object({
  search: z.string().trim().min(1).max(120).optional(),
  serviceId: z.string().cuid().optional(),
});

export function createApp(clinics: ClinicRepository) {
  const app = express();
  app.use(cors({ origin: env.FRONTEND_ORIGIN, methods: ['GET'], optionsSuccessStatus: 204 }));
  app.use(express.json());

  app.get('/api/health', (_request, response) => {
    response.json({ status: 'ok', service: 'DentalConnect API' });
  });

  app.get('/api/clinics', async (request, response, next) => {
    try {
      const query = clinicsQuerySchema.parse(request.query);
      response.json({ data: await clinics.listActive(query) });
    } catch (error) {
      next(error);
    }
  });

  app.get('/api/clinics/:id', async (request, response, next) => {
    try {
      const { id } = clinicIdSchema.parse(request.params);
      const clinic = await clinics.findActiveById(id);
      if (!clinic) {
        response
          .status(404)
          .json({ error: { code: 'CLINIC_NOT_FOUND', message: 'Consultorio no encontrado' } });
        return;
      }
      response.json({ data: clinic });
    } catch (error) {
      next(error);
    }
  });

  app.use((_request, response) => {
    response.status(404).json({ error: { code: 'NOT_FOUND', message: 'Ruta no encontrada' } });
  });

  app.use((error: unknown, _request: Request, response: Response, next: NextFunction) => {
    void next;
    if (error instanceof ZodError) {
      response.status(400).json({
        error: {
          code: 'VALIDATION_ERROR',
          message: 'Parámetros inválidos',
          details: error.flatten(),
        },
      });
      return;
    }
    console.error(error);
    response
      .status(500)
      .json({ error: { code: 'INTERNAL_ERROR', message: 'Error interno del servidor' } });
  });

  return app;
}
