import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../src/app.js';
import type {
  ClinicDetail,
  ClinicRepository,
  ClinicSearch,
  ClinicSummary,
} from '../src/types/clinic.js';

const clinic: ClinicDetail = {
  id: 'ckvclinic00000000000000001',
  name: 'Sonrisa del Centro',
  description: 'Consultorio ficticio.',
  phone: '222-555-0101',
  email: 'centro@ejemplo.dental',
  address: 'Av. Ejemplo 101',
  municipality: 'Puebla',
  state: 'Puebla',
  postalCode: '72000',
  latitude: 19.0433,
  longitude: -98.2019,
  imageUrl: null,
  services: [
    {
      id: 'ckvservice0000000000000001',
      name: 'Limpieza dental',
      description: 'Profilaxis ficticia.',
      estimatedDurationMinutes: 45,
      priceFrom: 700,
    },
  ],
  dentists: [
    {
      id: 'ckvdentist0000000000000001',
      fullName: 'Valeria Mendoza',
      professionalLicense: 'CED-FICT-1001',
      specialty: 'Odontología general',
      biography: 'Profesional ficticia.',
      imageUrl: null,
    },
  ],
};

class FakeClinicRepository implements ClinicRepository {
  filters?: ClinicSearch;
  clinics: ClinicSummary[] = [
    { ...clinic, services: clinic.services.map(({ id, name }) => ({ id, name })) },
  ];
  async listActive(filters: ClinicSearch) {
    this.filters = filters;
    return this.clinics;
  }
  async findActiveById(id: string) {
    return id === clinic.id ? clinic : null;
  }
}

describe('DentalConnect API', () => {
  let repository: FakeClinicRepository;
  beforeEach(() => {
    repository = new FakeClinicRepository();
  });

  it('returns the health check', async () => {
    const response = await request(createApp(repository)).get('/api/health');
    expect(response.status).toBe(200);
    expect(response.body).toEqual({ status: 'ok', service: 'DentalConnect API' });
  });

  it('lists active clinics and accepts search', async () => {
    const response = await request(createApp(repository)).get('/api/clinics?search=Puebla');
    expect(response.status).toBe(200);
    expect(response.body.data).toHaveLength(1);
    expect(repository.filters).toEqual({ search: 'Puebla' });
  });

  it('returns an empty list without an error', async () => {
    repository.clinics = [];
    const response = await request(createApp(repository)).get('/api/clinics');
    expect(response.status).toBe(200);
    expect(response.body.data).toEqual([]);
  });

  it('returns a clinic detail', async () => {
    const response = await request(createApp(repository)).get(`/api/clinics/${clinic.id}`);
    expect(response.status).toBe(200);
    expect(response.body.data.dentists[0].professionalLicense).toBe('CED-FICT-1001');
  });

  it('returns a consistent error for a missing clinic', async () => {
    const response = await request(createApp(repository)).get(
      '/api/clinics/ckvclinic00000000000000002',
    );
    expect(response.status).toBe(404);
    expect(response.body.error.code).toBe('CLINIC_NOT_FOUND');
  });

  it('returns a consistent route 404', async () => {
    const response = await request(createApp(repository)).get('/api/unknown');
    expect(response.status).toBe(404);
    expect(response.body.error.code).toBe('NOT_FOUND');
  });
});
