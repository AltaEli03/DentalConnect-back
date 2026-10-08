import bcrypt from 'bcrypt';
import crypto from 'node:crypto';
import jwt from 'jsonwebtoken';
import { env } from '../config/env.js';
import type { EmailService } from './email.service.js';

export type PublicUser = { id: string; firstName: string; lastName: string; email: string; phone: string; role: 'PATIENT'|'CLINIC_ADMIN'|'DENTIST'|'ADMIN'; isEmailVerified: boolean; isActive: boolean; createdAt: Date };
type StoredUser = PublicUser & { passwordHash: string };
type Verification = { id: string; userId: string; codeHash: string; expiresAt: Date; usedAt: Date | null; attempts: number; createdAt: Date };
export interface AuthRepository {
 findUserByEmail(email: string): Promise<StoredUser | null>; findUserById(id: string): Promise<StoredUser | null>; createUser(input: Omit<StoredUser, 'id'|'createdAt'>): Promise<StoredUser>;
 createCode(input: Omit<Verification, 'id'|'createdAt'|'usedAt'|'attempts'>): Promise<Verification>; latestCode(userId: string): Promise<Verification | null>; invalidateCodes(userId: string): Promise<void>; useCode(id: string): Promise<void>; incrementAttempts(id: string): Promise<void>; verifyUser(id: string): Promise<void>;
 createSession(input: {id:string;userId:string; tokenHash:string; expiresAt:Date}): Promise<void>; activeSession(id:string, tokenHash:string): Promise<boolean>; revokeSession(id:string): Promise<void>;
}
const hashToken = (value: string) => crypto.createHash('sha256').update(value).digest('hex');
const publicUser = (user: StoredUser): PublicUser => ({ id:user.id, firstName:user.firstName, lastName:user.lastName, email:user.email, phone:user.phone, role:user.role, isEmailVerified:user.isEmailVerified, isActive:user.isActive, createdAt:user.createdAt });
export class AuthService {
  constructor(private readonly repo: AuthRepository, private readonly email: EmailService) {}
  async register(input: {firstName:string;lastName:string;email:string;phone:string;password:string}) {
    const email = input.email.toLowerCase(); if (await this.repo.findUserByEmail(email)) return { conflict: true as const };
    const user = await this.repo.createUser({ ...input, email, passwordHash: await bcrypt.hash(input.password, 12), role: 'PATIENT', isEmailVerified: false, isActive: true });
    await this.issueCode(user); return { user: publicUser(user) };
  }
  async issueCode(user: StoredUser) { const code = crypto.randomInt(100000, 1000000).toString(); await this.repo.invalidateCodes(user.id); await this.repo.createCode({ userId: user.id, codeHash: await bcrypt.hash(code, 12), expiresAt: new Date(Date.now()+600000) }); await this.email.sendVerificationCode(user.email, user.firstName, code); }
  async verify(email: string, code: string) { const user=await this.repo.findUserByEmail(email.toLowerCase()); if (!user) return false; const record=await this.repo.latestCode(user.id); if (!record || record.usedAt || record.expiresAt < new Date() || record.attempts >= 5) return false; if (!(await bcrypt.compare(code,record.codeHash))) { await this.repo.incrementAttempts(record.id); return false; } await this.repo.useCode(record.id); await this.repo.invalidateCodes(user.id); await this.repo.verifyUser(user.id); return true; }
  async resend(email: string) { const user=await this.repo.findUserByEmail(email.toLowerCase()); if (!user || user.isEmailVerified) return; const previous=await this.repo.latestCode(user.id); if (previous && Date.now()-previous.createdAt.getTime()<60000) return; await this.issueCode(user); }
  async login(email: string, password: string) { const user=await this.repo.findUserByEmail(email.toLowerCase()); if (!user || !user.isActive || !user.isEmailVerified || !(await bcrypt.compare(password,user.passwordHash))) return null; const expiresAt=new Date(Date.now()+3600000); const sid=crypto.randomUUID(); const token=jwt.sign({id:user.id,email:user.email,role:user.role,sid},env.JWT_SECRET,{expiresIn:env.JWT_EXPIRES_IN as jwt.SignOptions['expiresIn']}); await this.repo.createSession({id:sid,userId:user.id,tokenHash:hashToken(token),expiresAt}); return { token, user: publicUser(user) }; }
  async session(token: string) { try { const decoded=jwt.verify(token,env.JWT_SECRET) as jwt.JwtPayload; if (!decoded.id || !decoded.sid || !(await this.repo.activeSession(decoded.sid,hashToken(token)))) return null; const user=await this.repo.findUserById(decoded.id); return user && user.isActive ? {user:publicUser(user),sid:decoded.sid} : null; } catch { return null; } }
  async logout(token?: string) { if (!token) return; try { const decoded=jwt.verify(token,env.JWT_SECRET) as jwt.JwtPayload; if (decoded.sid) await this.repo.revokeSession(decoded.sid); } catch { /* clear cookie anyway */ } }
}
