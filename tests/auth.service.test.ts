import { describe, expect, it } from 'vitest';
import { AuthService, type AuthRepository } from '../src/services/auth.service.js';
import type { EmailService } from '../src/services/email.service.js';

type TestUser = NonNullable<Awaited<ReturnType<AuthRepository['findUserById']>>>;
type TestCode = NonNullable<Awaited<ReturnType<AuthRepository['latestCode']>>>;

class MemoryAuthRepository implements AuthRepository {
  users: TestUser[] = [];
  codes: TestCode[] = [];
  sessions = new Map<string, { userId: string; tokenHash: string; expiresAt: Date; revoked: boolean }>();

  async findUserByEmail(email: string) {
    return this.users.find((user) => user.email === email) ?? null;
  }
  async findUserById(id: string) {
    return this.users.find((user) => user.id === id) ?? null;
  }
  async createUser(input: Parameters<AuthRepository['createUser']>[0]) {
    const user = { ...input, id: `user-${this.users.length + 1}`, createdAt: new Date() };
    this.users.push(user);
    return user;
  }
  async createCode(input: Parameters<AuthRepository['createCode']>[0]) {
    const code = {
      ...input,
      id: `code-${this.codes.length + 1}`,
      createdAt: new Date(),
      usedAt: null,
      attempts: 0,
    };
    this.codes.push(code);
    return code;
  }
  async latestCode(userId: string) {
    return this.codes.filter((code) => code.userId === userId).at(-1) ?? null;
  }
  async invalidateCodes(userId: string) {
    for (const code of this.codes) {
      if (code.userId === userId && !code.usedAt) code.usedAt = new Date();
    }
  }
  async useCode(id: string) {
    const code = this.codes.find((item) => item.id === id);
    if (code) code.usedAt = new Date();
  }
  async incrementAttempts(id: string) {
    const code = this.codes.find((item) => item.id === id);
    if (code) code.attempts += 1;
  }
  async verifyUser(id: string) {
    const user = await this.findUserById(id);
    if (user) user.isEmailVerified = true;
  }
  async createSession(input: { id: string; userId: string; tokenHash: string; expiresAt: Date }) {
    this.sessions.set(input.id, { ...input, revoked: false });
  }
  async activeSession(id: string, tokenHash: string) {
    const session = this.sessions.get(id);
    return !!session && session.tokenHash === tokenHash && !session.revoked && session.expiresAt > new Date();
  }
  async revokeSession(id: string) {
    const session = this.sessions.get(id);
    if (session) session.revoked = true;
  }
}

class MemoryEmailService implements EmailService {
  sent: Array<{ email: string; code: string }> = [];
  async sendVerificationCode(email: string, _firstName: string, code: string) {
    this.sent.push({ email, code });
  }
}

describe('Sprint 2 authentication service', () => {
  it('creates an unverified patient and stores only a hash of the code', async () => {
    const repo = new MemoryAuthRepository();
    const mail = new MemoryEmailService();
    const auth = new AuthService(repo, mail);

    const result = await auth.register({
      firstName: 'Ana',
      lastName: 'Pérez',
      email: 'ANA@example.com',
      phone: '+522221234567',
      password: 'DentConnect123!',
    });

    expect('conflict' in result).toBe(false);
    if ('conflict' in result) return;
    expect(result.user).toMatchObject({ email: 'ana@example.com', role: 'PATIENT', isEmailVerified: false });
    expect(mail.sent).toHaveLength(1);
    expect(mail.sent[0].code).toMatch(/^\d{6}$/);
    expect(repo.codes[0].codeHash).not.toBe(mail.sent[0].code);
    expect(JSON.stringify(result)).not.toContain(mail.sent[0].code);
  });

  it('prevents duplicate registration and blocks login until verification', async () => {
    const repo = new MemoryAuthRepository();
    const mail = new MemoryEmailService();
    const auth = new AuthService(repo, mail);
    const input = {
      firstName: 'Ana',
      lastName: 'Pérez',
      email: 'ana@example.com',
      phone: '+522221234567',
      password: 'DentConnect123!',
    };

    await auth.register(input);
    expect(await auth.register(input)).toEqual({ conflict: true });
    expect(await auth.login(input.email, input.password)).toBeNull();
  });

  it('counts invalid codes, activates with the correct code and revokes the session at logout', async () => {
    const repo = new MemoryAuthRepository();
    const mail = new MemoryEmailService();
    const auth = new AuthService(repo, mail);
    const input = {
      firstName: 'Ana',
      lastName: 'Pérez',
      email: 'ana@example.com',
      phone: '+522221234567',
      password: 'DentConnect123!',
    };
    const registered = await auth.register(input);
    expect('conflict' in registered).toBe(false);

    expect(await auth.verify(input.email, '000000')).toBe(false);
    expect(repo.codes[0].attempts).toBe(1);
    expect(await auth.verify(input.email, mail.sent[0].code)).toBe(true);
    const login = await auth.login(input.email, input.password);
    expect(login?.user).toMatchObject({ email: input.email, role: 'PATIENT', isEmailVerified: true });
    expect(login?.token).toBeTruthy();

    const active = await auth.session(login!.token);
    expect(active?.user.email).toBe(input.email);
    await auth.logout(login!.token);
    expect(await auth.session(login!.token)).toBeNull();
  });
});
