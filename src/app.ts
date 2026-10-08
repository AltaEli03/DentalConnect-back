import cookieParser from 'cookie-parser';
import cors from 'cors';
import express, { type NextFunction, type Request, type Response } from 'express';
import rateLimit from 'express-rate-limit';
import { ZodError, z } from 'zod';
import { env } from './config/env.js';
import type { AuthService } from './services/auth.service.js';
import type { ClinicRepository } from './types/clinic.js';

const clinicIdSchema = z.object({ id: z.string().cuid() });
const clinicsQuerySchema = z.object({ search: z.string().trim().min(1).max(120).optional(), serviceId: z.string().cuid().optional() });
const registrationSchema = z.object({ firstName:z.string().trim().min(2).max(80), lastName:z.string().trim().min(2).max(80), email:z.string().trim().email(), phone:z.string().trim().min(8).max(20), password:z.string().min(12).max(128), confirmPassword:z.string() }).refine((data)=>data.password===data.confirmPassword,{path:['confirmPassword'],message:'Las contraseñas no coinciden'});
const verifySchema=z.object({email:z.string().trim().email(),code:z.string().regex(/^\d{6}$/)});
const loginSchema=z.object({email:z.string().trim().email(),password:z.string().min(1).max(128)});
const emailSchema=z.object({email:z.string().trim().email()});
const authLimiter=rateLimit({windowMs:15*60*1000,limit:20,standardHeaders:true,legacyHeaders:false,message:{error:{code:'RATE_LIMITED',message:'Demasiados intentos; inténtalo más tarde'}}});

export function createApp(clinics: ClinicRepository, auth?: AuthService) {
  const app = express();
  app.use(cors({ origin: [env.FRONTEND_ORIGIN, env.FRONTEND_URL], credentials: true, methods: ['GET','POST'], optionsSuccessStatus: 204 }));
  app.use(express.json()); app.use(cookieParser());
  const requiredAuth=(): AuthService => { if (!auth) throw new Error('AUTH_UNAVAILABLE'); return auth; };
  const setCookie=(response:Response,token:string)=>response.cookie('dentalconnect_session',token,{httpOnly:true,secure:env.COOKIE_SECURE,sameSite:env.COOKIE_SAME_SITE,path:'/',maxAge:3600000});
  app.get('/api/health', (_request, response) => response.json({ status: 'ok', service: 'DentalConnect API' }));
  app.get('/api/clinics', async (request,response,next)=>{try{response.json({data:await clinics.listActive(clinicsQuerySchema.parse(request.query))});}catch(error){next(error);}});
  app.get('/api/clinics/:id', async (request,response,next)=>{try{const clinic=await clinics.findActiveById(clinicIdSchema.parse(request.params).id);if(!clinic){response.status(404).json({error:{code:'CLINIC_NOT_FOUND',message:'Consultorio no encontrado'}});return;}response.json({data:clinic});}catch(error){next(error);}});
  app.post('/api/auth/register',authLimiter,async(request,response,next)=>{try{const data=registrationSchema.parse(request.body);const result=await requiredAuth().register(data);if('conflict' in result){response.status(409).json({error:{code:'EMAIL_EXISTS',message:'No fue posible completar el registro'}});return;}response.status(201).json({data:{user:result.user}});}catch(error){next(error);}});
  app.post('/api/auth/verify-email',authLimiter,async(request,response,next)=>{try{const data=verifySchema.parse(request.body);if(!(await requiredAuth().verify(data.email,data.code))){response.status(400).json({error:{code:'INVALID_VERIFICATION_CODE',message:'El código no es válido o expiró'}});return;}response.json({data:{message:'Correo verificado'}});}catch(error){next(error);}});
  app.post('/api/auth/resend-verification-code',authLimiter,async(request,response,next)=>{try{await requiredAuth().resend(emailSchema.parse(request.body).email);response.status(202).json({data:{message:'Si la cuenta requiere verificación, se enviará un código'}});}catch(error){next(error);}});
  app.post('/api/auth/login',authLimiter,async(request,response,next)=>{try{const data=loginSchema.parse(request.body);const result=await requiredAuth().login(data.email,data.password);if(!result){response.status(401).json({error:{code:'INVALID_CREDENTIALS',message:'Credenciales inválidas o cuenta sin verificar'}});return;}setCookie(response,result.token);response.json({data:{user:result.user}});}catch(error){next(error);}});
  app.post('/api/auth/logout',async(request,response,next)=>{try{await requiredAuth().logout(request.cookies.dentalconnect_session as string|undefined);response.clearCookie('dentalconnect_session',{httpOnly:true,secure:env.COOKIE_SECURE,sameSite:env.COOKIE_SAME_SITE,path:'/'}).status(204).end();}catch(error){next(error);}});
  app.get('/api/auth/me',async(request,response,next)=>{try{const token=request.cookies.dentalconnect_session as string|undefined;const session=await requiredAuth().session(token ?? '');if(!session){response.status(401).json({error:{code:'UNAUTHENTICATED',message:'Sesión requerida'}});return;}response.json({data:{user:session.user}});}catch(error){next(error);}});
  app.use((_request,response)=>response.status(404).json({error:{code:'NOT_FOUND',message:'Ruta no encontrada'}}));
  app.use((error:unknown,_request:Request,response:Response,next:NextFunction)=>{void next;if(error instanceof ZodError){response.status(400).json({error:{code:'VALIDATION_ERROR',message:'Datos inválidos',details:error.flatten()}});return;}if(error instanceof Error&&error.message==='AUTH_UNAVAILABLE'){response.status(503).json({error:{code:'AUTH_UNAVAILABLE',message:'Autenticación no disponible'}});return;}if(env.NODE_ENV!=='production') console.error(error);response.status(500).json({error:{code:'INTERNAL_ERROR',message:'Error interno del servidor'}});});
  return app;
}
