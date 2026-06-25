import { TestBed } from '@angular/core/testing';

import { NotificationService } from './notification.service';

describe('NotificationService', () => {
  let svc: NotificationService;

  beforeEach(() => {
    vi.useFakeTimers();
    svc = TestBed.inject(NotificationService);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('shows a notification with the default info type', () => {
    svc.show('hello');
    expect(svc.notifications()).toEqual([{ id: 0, message: 'hello', type: 'info' }]);
  });

  it('shows with an explicit type and increments ids', () => {
    svc.show('a', 'success');
    svc.show('b', 'danger');
    const list = svc.notifications();
    expect(list).toHaveLength(2);
    expect(list[1]).toEqual({ id: 1, message: 'b', type: 'danger' });
  });

  it('auto-dismisses after 2 seconds', () => {
    svc.show('x');
    expect(svc.notifications()).toHaveLength(1);
    vi.advanceTimersByTime(2000);
    expect(svc.notifications()).toHaveLength(0);
  });

  it('dismiss removes a specific notification by id', () => {
    svc.show('a');
    svc.show('b');
    svc.dismiss(0);
    expect(svc.notifications().map((n) => n.message)).toEqual(['b']);
  });
});
