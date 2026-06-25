import { TestBed } from '@angular/core/testing';

import { EVENT_SOURCE_FACTORY } from './event-source.factory';

describe('EVENT_SOURCE_FACTORY', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('default factory opens an EventSource at the given url', () => {
    const opened: string[] = [];
    class FakeEventSource {
      constructor(public url: string) {
        opened.push(url);
      }
    }
    vi.stubGlobal('EventSource', FakeEventSource);

    const factory = TestBed.inject(EVENT_SOURCE_FACTORY);
    const es = factory('http://localhost/api/v1/ocr-results');

    expect(es).toBeInstanceOf(FakeEventSource);
    expect(opened).toEqual(['http://localhost/api/v1/ocr-results']);
  });
});
