import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url.token';

@Injectable({
  providedIn: 'root',
})
export class ApiClientService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get<TResponse>(path: string): Observable<TResponse> {
    return this.http.get<TResponse>(this.buildUrl(path));
  }

  post<TRequest, TResponse>(path: string, body: TRequest): Observable<TResponse> {
    return this.http.post<TResponse>(this.buildUrl(path), body);
  }

  put<TRequest, TResponse>(path: string, body: TRequest): Observable<TResponse> {
    return this.http.put<TResponse>(this.buildUrl(path), body);
  }

  delete(path: string): Observable<void> {
    return this.http.delete<void>(this.buildUrl(path));
  }

  private buildUrl(path: string): string {
    return new URL(path, this.baseUrl).toString();
  }
}
