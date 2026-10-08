import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

async function main() {
  await prisma.clinicService.deleteMany();
  await prisma.dentist.deleteMany();
  await prisma.clinic.deleteMany();
  await prisma.dentalService.deleteMany();

  const services = await Promise.all([
    prisma.dentalService.create({
      data: {
        name: 'Consulta de valoración',
        description: 'Evaluación inicial ficticia de salud bucal.',
        estimatedDurationMinutes: 30,
      },
    }),
    prisma.dentalService.create({
      data: {
        name: 'Limpieza dental',
        description: 'Profilaxis preventiva ficticia.',
        estimatedDurationMinutes: 45,
      },
    }),
    prisma.dentalService.create({
      data: {
        name: 'Ortodoncia',
        description: 'Valoración ficticia para alineación dental.',
        estimatedDurationMinutes: 50,
      },
    }),
    prisma.dentalService.create({
      data: {
        name: 'Endodoncia',
        description: 'Atención ficticia de conductos radiculares.',
        estimatedDurationMinutes: 90,
      },
    }),
    prisma.dentalService.create({
      data: {
        name: 'Odontopediatría',
        description: 'Consulta ficticia para niñas y niños.',
        estimatedDurationMinutes: 40,
      },
    }),
    prisma.dentalService.create({
      data: {
        name: 'Blanqueamiento dental',
        description: 'Procedimiento estético ficticio.',
        estimatedDurationMinutes: 60,
      },
    }),
  ]);
  const byName = Object.fromEntries(services.map((service) => [service.name, service]));

  const clinics = await Promise.all([
    prisma.clinic.create({
      data: {
        name: 'Sonrisa del Centro',
        description: 'Consultorio ficticio de atención dental general en el centro de Puebla.',
        phone: '222-555-0101',
        email: 'centro@ejemplo.dental',
        address: 'Av. Ejemplo 101, Col. Centro',
        municipality: 'Puebla',
        state: 'Puebla',
        postalCode: '72000',
        latitude: 19.0433,
        longitude: -98.2019,
        imageUrl:
          'https://images.unsplash.com/photo-1629909613654-28e377c37b09?auto=format&fit=crop&w=1200&q=80',
      },
    }),
    prisma.clinic.create({
      data: {
        name: 'Clínica Horizonte Dental',
        description: 'Consultorio ficticio enfocado en prevención y estética dental.',
        phone: '55-5555-0102',
        email: 'horizonte@ejemplo.dental',
        address: 'Calle Horizonte 220, Col. Del Valle',
        municipality: 'Benito Juárez',
        state: 'Ciudad de México',
        postalCode: '03100',
        latitude: 19.387,
        longitude: -99.162,
        imageUrl:
          'https://images.unsplash.com/photo-1606811971618-4486d14f3f99?auto=format&fit=crop&w=1200&q=80',
      },
    }),
    prisma.clinic.create({
      data: {
        name: 'Dental Norte Familiar',
        description: 'Consultorio ficticio para familias y atención odontopediátrica.',
        phone: '81-5555-0103',
        email: 'norte@ejemplo.dental',
        address: 'Av. Norte 450, Col. Cumbres',
        municipality: 'Monterrey',
        state: 'Nuevo León',
        postalCode: '64610',
        latitude: 25.714,
        longitude: -100.367,
        imageUrl:
          'https://images.unsplash.com/photo-1588776814546-daab30f31062?auto=format&fit=crop&w=1200&q=80',
      },
    }),
  ]);

  await prisma.clinicService.createMany({
    data: [
      {
        clinicId: clinics[0].id,
        dentalServiceId: byName['Consulta de valoración'].id,
        priceFrom: 450,
      },
      { clinicId: clinics[0].id, dentalServiceId: byName['Limpieza dental'].id, priceFrom: 700 },
      {
        clinicId: clinics[0].id,
        dentalServiceId: byName['Blanqueamiento dental'].id,
        priceFrom: 2600,
      },
      {
        clinicId: clinics[1].id,
        dentalServiceId: byName['Consulta de valoración'].id,
        priceFrom: 500,
      },
      { clinicId: clinics[1].id, dentalServiceId: byName['Ortodoncia'].id, priceFrom: 650 },
      { clinicId: clinics[1].id, dentalServiceId: byName['Endodoncia'].id, priceFrom: 3200 },
      {
        clinicId: clinics[2].id,
        dentalServiceId: byName['Consulta de valoración'].id,
        priceFrom: 400,
      },
      { clinicId: clinics[2].id, dentalServiceId: byName['Odontopediatría'].id, priceFrom: 600 },
      { clinicId: clinics[2].id, dentalServiceId: byName['Limpieza dental'].id, priceFrom: 650 },
    ],
  });

  await prisma.dentist.createMany({
    data: [
      {
        clinicId: clinics[0].id,
        firstName: 'Valeria',
        lastName: 'Mendoza',
        professionalLicense: 'CED-FICT-1001',
        specialty: 'Odontología general',
        biography: 'Profesional ficticia enfocada en prevención y atención integral.',
        imageUrl:
          'https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=480&q=80',
      },
      {
        clinicId: clinics[0].id,
        firstName: 'Jorge',
        lastName: 'Paredes',
        professionalLicense: 'CED-FICT-1002',
        specialty: 'Estética dental',
        biography: 'Profesional ficticio con interés en rehabilitación estética.',
        imageUrl: null,
      },
      {
        clinicId: clinics[1].id,
        firstName: 'Camila',
        lastName: 'Ríos',
        professionalLicense: 'CED-FICT-1003',
        specialty: 'Ortodoncia',
        biography: 'Profesional ficticia dedicada a tratamientos de alineación dental.',
        imageUrl:
          'https://images.unsplash.com/photo-1594824476967-48c8b964273f?auto=format&fit=crop&w=480&q=80',
      },
      {
        clinicId: clinics[2].id,
        firstName: 'Mateo',
        lastName: 'León',
        professionalLicense: 'CED-FICT-1004',
        specialty: 'Odontopediatría',
        biography: 'Profesional ficticio enfocado en una experiencia amable para infancias.',
        imageUrl: null,
      },
    ],
  });
}

main()
  .then(() => console.log('Seed ficticio completado.'))
  .finally(async () => prisma.$disconnect());
