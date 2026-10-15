import { afterEach, describe, expect, it, vi } from 'vitest';

afterEach(() => {
  vi.unstubAllEnvs();
  vi.doUnmock('../src/config/env.js');
  vi.resetModules();
});

describe('configured verification email delivery', () => {
  it('sends verification messages through SES and escapes the HTML name', async () => {
    vi.stubEnv('EMAIL_PROVIDER', 'ses');
    vi.stubEnv('EMAIL_FROM', 'no-reply@example.com');
    vi.stubEnv('AWS_REGION', 'us-east-1');
    vi.doMock('../src/config/env.js', () => ({
      env: {
        NODE_ENV: 'test',
        EMAIL_PROVIDER: 'ses',
        EMAIL_FROM: 'no-reply@example.com',
        AWS_REGION: 'us-east-1',
      },
    }));
    vi.resetModules();
    const { ConfiguredEmailService } = await import('../src/services/email.service.js');
    const sendSes = vi.fn().mockResolvedValue({});
    const service = new ConfiguredEmailService(sendSes);

    await service.sendVerificationCode('patient@example.com', '<Ana & Luis>', '482106');

    const command = sendSes.mock.calls[0][0];
    expect(command.input).toMatchObject({
      FromEmailAddress: 'no-reply@example.com',
      Destination: { ToAddresses: ['patient@example.com'] },
      Content: {
        Simple: {
          Subject: { Data: 'Verifica tu cuenta de DentalConnect' },
          Body: {
            Text: { Data: expect.stringContaining('482106') },
            Html: { Data: expect.stringContaining('&lt;Ana &amp; Luis&gt;') },
          },
        },
      },
    });
  });

  it('prints the development-only verification code to the console for local testing', async () => {
    vi.stubEnv('EMAIL_PROVIDER', 'console');
    vi.stubEnv('NODE_ENV', 'development');
    vi.doMock('../src/config/env.js', () => ({
      env: {
        NODE_ENV: 'development',
        EMAIL_PROVIDER: 'console',
        EMAIL_FROM: 'no-reply@example.com',
        AWS_REGION: 'us-east-1',
      },
    }));
    vi.resetModules();
    const { ConfiguredEmailService } = await import('../src/services/email.service.js');
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);

    await new ConfiguredEmailService().sendVerificationCode('patient@example.com', 'Ana', '482106');

    expect(info).toHaveBeenCalledWith(
      '[email] Código de verificación para patient@example.com: 482106 (solo desarrollo)',
    );
  });
});
