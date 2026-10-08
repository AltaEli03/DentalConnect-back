import nodemailer from 'nodemailer';
import { env } from '../config/env.js';

export interface EmailService { sendVerificationCode(email: string, firstName: string, code: string): Promise<void>; }

export class ConfiguredEmailService implements EmailService {
  async sendVerificationCode(email: string, firstName: string, code: string) {
    const subject = 'Verifica tu cuenta de DentalConnect';
    const html = `<p>Hola ${firstName},</p><p>Tu código de verificación es <strong>${code}</strong>.</p><p>Expira en 10 minutos.</p>`;
    if (env.EMAIL_PROVIDER === 'console') {
      console.info(`[email] Verification code generated for ${email}`);
      return;
    }
    if (env.EMAIL_PROVIDER === 'smtp') {
      if (!env.SMTP_HOST || !env.SMTP_PORT || !env.SMTP_USER || !env.SMTP_PASSWORD) throw new Error('SMTP no está configurado');
      const transport = nodemailer.createTransport({ host: env.SMTP_HOST, port: env.SMTP_PORT, auth: { user: env.SMTP_USER, pass: env.SMTP_PASSWORD } });
      await transport.sendMail({ from: env.EMAIL_FROM, to: email, subject, html });
      return;
    }
    // SES is intentionally configured at deployment time; never fall back to credentials in code.
    throw new Error('El proveedor SES requiere una integración AWS configurada');
  }
}
