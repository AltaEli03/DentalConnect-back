import nodemailer from 'nodemailer';
import { env } from '../config/env.js';

export interface EmailService { sendVerificationCode(email: string, firstName: string, code: string): Promise<void>; }

const escapeHtml = (value: string) => value.replace(/[&<>"']/g, (character) => ({
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
})[character]!);

export class ConfiguredEmailService implements EmailService {
  async sendVerificationCode(email: string, firstName: string, code: string) {
    const subject = 'Verifica tu cuenta de DentalConnect';
    const html = `<p>Hola ${escapeHtml(firstName)},</p><p>Tu código de verificación es <strong>${code}</strong>.</p><p>Expira en 10 minutos.</p>`;
    if (env.EMAIL_PROVIDER === 'console') {
      if (env.NODE_ENV === 'production') throw new Error('El modo de correo por consola no está permitido en producción');
      console.info(`[email] Código de verificación para ${email}: ${code} (solo desarrollo)`);
      return;
    }
    if (!env.SMTP_HOST || !env.SMTP_PORT || !env.SMTP_USER || !env.SMTP_PASSWORD) throw new Error('SMTP no está configurado');
    const transport = nodemailer.createTransport({
      host: env.SMTP_HOST,
      port: env.SMTP_PORT,
      secure: env.SMTP_PORT === 465,
      auth: { user: env.SMTP_USER, pass: env.SMTP_PASSWORD },
    });
    await transport.sendMail({ from: env.EMAIL_FROM, to: email, subject, html });
  }
}
