import { Outlet } from 'react-router-dom';
import { AppNavbar } from './AppNavbar';

export function AppShell() {
  return (
    <div className="app-shell">
      <AppNavbar />
      <main className="app-shell__content">
        <Outlet />
      </main>
    </div>
  );
}
