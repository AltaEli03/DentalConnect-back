import { createApp } from './app.js';
import { env } from './config/env.js';
import { PrismaClinicRepository } from './repositories/prisma-clinic-repository.js';

const app = createApp(new PrismaClinicRepository());

app.listen(env.PORT, () => {
  console.log(`DentalConnect API listening on port ${env.PORT}`);
});
