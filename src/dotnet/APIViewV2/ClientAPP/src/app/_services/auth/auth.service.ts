import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  baseUrl : string = "https://localhost:57816/api/auth";

  constructor(private http: HttpClient) { }

  isLoggedIn() {
    return this.http.get(this.baseUrl, { withCredentials: true });
  }
}
