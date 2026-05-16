import { NavLink } from 'react-router-dom';

const navLinkClass = ({ isActive }: { isActive: boolean }): string =>
  `app-navbar__link${isActive ? ' is-active' : ''}`;

export function AppNavbar() {
  return (
    <nav className="app-navbar">
      <NavLink to="/documents" className="app-navbar__brand">
        Paperless
      </NavLink>
      <ul className="app-navbar__links">
        <li>
          <NavLink to="/documents" className={navLinkClass}>
            Documents
          </NavLink>
        </li>
        <li>
          <NavLink to="/upload" className={navLinkClass}>
            Upload
          </NavLink>
        </li>
        <li>
          <NavLink to="/search" className={navLinkClass}>
            Search
          </NavLink>
        </li>
      </ul>
    </nav>
  );
}
