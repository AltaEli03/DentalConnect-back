import { prisma } from '../lib/prisma.js';
import type { AuthRepository } from '../services/auth.service.js';

export class PrismaAuthRepository implements AuthRepository {
  findUserByEmail(email: string) { return prisma.user.findUnique({ where: { email } }); }
  findUserById(id: string) { return prisma.user.findUnique({ where: { id } }); }
  createUser(input: Parameters<AuthRepository['createUser']>[0]) { return prisma.user.create({ data: input }); }
  createCode(input: Parameters<AuthRepository['createCode']>[0]) { return prisma.emailVerificationCode.create({ data: input }); }
  latestCode(userId: string) { return prisma.emailVerificationCode.findFirst({ where: { userId }, orderBy: { createdAt: 'desc' } }); }
  async invalidateCodes(userId: string) { await prisma.emailVerificationCode.updateMany({ where: { userId, usedAt: null }, data: { usedAt: new Date() } }); }
  async useCode(id: string) { await prisma.emailVerificationCode.update({ where: { id }, data: { usedAt: new Date() } }); }
  async incrementAttempts(id: string) { await prisma.emailVerificationCode.update({ where: { id }, data: { attempts: { increment: 1 } } }); }
  async verifyUser(id: string) { await prisma.user.update({ where: { id }, data: { isEmailVerified: true } }); }
  async createSession(input: Parameters<AuthRepository['createSession']>[0]) { await prisma.session.create({ data: input }); }
  async activeSession(id: string, tokenHash: string) { return !!(await prisma.session.findFirst({ where: { id, tokenHash, revokedAt: null, expiresAt: { gt: new Date() } } })); }
  async revokeSession(id: string) { await prisma.session.updateMany({ where: { id }, data: { revokedAt: new Date() } }); }
}
