import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'documents' },
  {
    path: 'documents',
    loadComponent: () =>
      import('./features/documents/pages/documents-page.component').then(
        (m) => m.DocumentsPageComponent,
      ),
  },
  { path: '**', redirectTo: 'documents' },
];
