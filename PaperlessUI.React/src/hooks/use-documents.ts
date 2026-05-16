import { useQuery } from '@tanstack/react-query';
import { fetchJson } from '@/lib/api';

export interface DocumentSummary {
  id: string;
  fileName: string;
  uploadedAtUtc: string;
}

export function useDocuments() {
  return useQuery({
    queryKey: ['documents'],
    queryFn: () => fetchJson<DocumentSummary[]>('/api/documents'),
  });
}
