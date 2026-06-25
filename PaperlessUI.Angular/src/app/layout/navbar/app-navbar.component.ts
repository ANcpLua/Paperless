import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { DocumentsService } from '../../features/documents/data/documents.service';
import { ThemeService } from '../../core/theme/theme.service';

@Component({
  selector: 'app-navbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-navbar.component.html',
})
export class AppNavbarComponent {
  protected readonly theme = inject(ThemeService);
  protected readonly docs = inject(DocumentsService);
}
