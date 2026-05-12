import { useDocuments } from '@/hooks/use-documents';

export function DocumentsPage() {
  const { data, isLoading, error } = useDocuments();

  if (isLoading) return <p>Loading documents…</p>;
  if (error) return <p role="alert">Failed to load documents.</p>;
  if (!data || data.length === 0) return <p>No documents yet — start by uploading one.</p>;

  return (
    <ul className="documents-list">
      {data.map((doc) => (
        <li key={doc.id}>
          <strong>{doc.fileName}</strong> — {new Date(doc.uploadedAtUtc).toLocaleString()}
        </li>
      ))}
    </ul>
  );
}
