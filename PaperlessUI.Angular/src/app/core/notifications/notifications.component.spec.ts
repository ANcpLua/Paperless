import { TestBed } from '@angular/core/testing';

import { NotificationService } from './notification.service';
import { NotificationsComponent } from './notifications.component';

describe('NotificationsComponent', () => {
  let service: NotificationService;

  beforeEach(async () => {
    vi.useFakeTimers();
    await TestBed.configureTestingModule({ imports: [NotificationsComponent] }).compileComponents();
    service = TestBed.inject(NotificationService);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('renders nothing when there are no notifications', () => {
    const f = TestBed.createComponent(NotificationsComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.alert').length).toBe(0);
  });

  it('renders a notification with its type class and message', () => {
    service.show('boom', 'danger');
    const f = TestBed.createComponent(NotificationsComponent);
    f.detectChanges();
    const alert = f.nativeElement.querySelector('.alert');
    expect(alert.classList).toContain('alert-danger');
    expect(alert.textContent).toContain('boom');
  });

  it('dismisses a notification when the close button is clicked', () => {
    service.show('bye', 'info');
    const f = TestBed.createComponent(NotificationsComponent);
    f.detectChanges();
    (f.nativeElement.querySelector('.btn-close') as HTMLButtonElement).click();
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.alert').length).toBe(0);
  });
});
