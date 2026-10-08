import { Prisma } from '@prisma/client';
import { prisma } from '../lib/prisma.js';
import type {
  ClinicDetail,
  ClinicRepository,
  ClinicSearch,
  ClinicSummary,
} from '../types/clinic.js';

const summaryInclude = {
  clinicServices: {
    where: { isActive: true, dentalService: { isActive: true } },
    select: { dentalService: { select: { id: true, name: true } } },
  },
} satisfies Prisma.ClinicInclude;

export class PrismaClinicRepository implements ClinicRepository {
  async listActive(filters: ClinicSearch): Promise<ClinicSummary[]> {
    const search = filters.search?.trim();
    const where: Prisma.ClinicWhereInput = {
      isActive: true,
      ...(search
        ? {
            OR: [
              { name: { contains: search, mode: 'insensitive' } },
              { municipality: { contains: search, mode: 'insensitive' } },
              { state: { contains: search, mode: 'insensitive' } },
            ],
          }
        : {}),
      ...(filters.serviceId
        ? {
            clinicServices: {
              some: {
                isActive: true,
                dentalServiceId: filters.serviceId,
                dentalService: { isActive: true },
              },
            },
          }
        : {}),
    };
    const clinics = await prisma.clinic.findMany({
      where,
      include: summaryInclude,
      orderBy: { name: 'asc' },
    });
    return clinics.map((clinic) => ({
      id: clinic.id,
      name: clinic.name,
      municipality: clinic.municipality,
      state: clinic.state,
      address: clinic.address,
      imageUrl: clinic.imageUrl,
      services: clinic.clinicServices.map((item) => item.dentalService),
    }));
  }

  async findActiveById(id: string): Promise<ClinicDetail | null> {
    const clinic = await prisma.clinic.findFirst({
      where: { id, isActive: true },
      include: {
        clinicServices: {
          where: { isActive: true, dentalService: { isActive: true } },
          include: { dentalService: true },
        },
        dentists: {
          where: { isActive: true },
          orderBy: [{ lastName: 'asc' }, { firstName: 'asc' }],
        },
      },
    });
    if (!clinic) return null;
    return {
      id: clinic.id,
      name: clinic.name,
      description: clinic.description,
      phone: clinic.phone,
      email: clinic.email,
      address: clinic.address,
      municipality: clinic.municipality,
      state: clinic.state,
      postalCode: clinic.postalCode,
      latitude: clinic.latitude,
      longitude: clinic.longitude,
      imageUrl: clinic.imageUrl,
      services: clinic.clinicServices.map((item) => ({
        id: item.dentalService.id,
        name: item.dentalService.name,
        description: item.dentalService.description,
        estimatedDurationMinutes: item.dentalService.estimatedDurationMinutes,
        priceFrom: Number(item.priceFrom),
      })),
      dentists: clinic.dentists.map((dentist) => ({
        id: dentist.id,
        fullName: `${dentist.firstName} ${dentist.lastName}`,
        professionalLicense: dentist.professionalLicense,
        specialty: dentist.specialty,
        biography: dentist.biography,
        imageUrl: dentist.imageUrl,
      })),
    };
  }
}
