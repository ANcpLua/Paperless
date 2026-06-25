import { ChangeDetectionStrategy, Component } from '@angular/core';

import { NotificationsComponent } from './core/notifications/notifications.component';
import { AppShellComponent } from './layout/shell/app-shell.component';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShellComponent, NotificationsComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
})
export class AppComponent {}
