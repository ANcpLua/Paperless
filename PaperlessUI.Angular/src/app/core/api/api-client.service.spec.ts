import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL } from '../config/api-base-url.token';
import { ApiClientService } from './api-client.service';

describe('ApiClientService', () => {
  let svc: ApiClientService;
  let httpTesting: HttpTestingController;
  const baseUrl = 'http://localhost:4200/';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: baseUrl },
      ],
    });
    svc = TestBed.inject(ApiClientService);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('GET resolves the path against the base URL', () => {
    svc.get('api/v1/documents').subscribe();
    const req = httpTesting.expectOne('http://localhost:4200/api/v1/documents');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('POST sends the body', () => {
    const body = { a: 1 };
    svc.post('api/x', body).subscribe();
    const req = httpTesting.expectOne('http://localhost:4200/api/x');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBe(body);
    req.flush({});
  });

  it('PUT sends the body', () => {
    svc.put('api/y', { b: 2 }).subscribe();
    const req = httpTesting.expectOne('http://localhost:4200/api/y');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ b: 2 });
    req.flush({});
  });

  it('DELETE issues a delete request', () => {
    svc.delete('api/z/1').subscribe();
    const req = httpTesting.expectOne('http://localhost:4200/api/z/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
