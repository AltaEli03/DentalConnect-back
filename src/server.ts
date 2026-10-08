import { createApp } from './app.js';
import { env } from './config/env.js';
import { PrismaClinicRepository } from './repositories/prisma-clinic-repository.js';
import { PrismaAuthRepository } from './repositories/prisma-auth-repository.js';
import { AuthService } from './services/auth.service.js';
import { ConfiguredEmailService } from './services/email.service.js';

const app = createApp(new PrismaClinicRepository(), new AuthService(new PrismaAuthRepository(), new ConfiguredEmailService()));

app.listen(env.PORT, () => {
  console.log(`DentalConnect API listening on port ${env.PORT}`);
});
