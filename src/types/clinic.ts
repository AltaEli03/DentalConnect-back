export type ClinicSummary = {
  id: string;
  name: string;
  municipality: string;
  state: string;
  address: string;
  imageUrl: string | null;
  services: Array<{ id: string; name: string }>;
};

export type ClinicDetail = ClinicSummary & {
  description: string;
  phone: string;
  email: string;
  postalCode: string;
  latitude: number | null;
  longitude: number | null;
  services: Array<{
    id: string;
    name: string;
    description: string;
    estimatedDurationMinutes: number;
    priceFrom: number;
  }>;
  dentists: Array<{
    id: string;
    fullName: string;
    professionalLicense: string;
    specialty: string;
    biography: string;
    imageUrl: string | null;
  }>;
};

export type ClinicSearch = { search?: string; serviceId?: string };

export interface ClinicRepository {
  listActive(filters: ClinicSearch): Promise<ClinicSummary[]>;
  findActiveById(id: string): Promise<ClinicDetail | null>;
}
