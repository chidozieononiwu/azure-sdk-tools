import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Review } from 'src/app/_models/review';

@Injectable({
  providedIn: 'root'
})
export class ReviewsService {
  baseUrl : string = "https://localhost:54007/api/reviews";

  constructor(private http: HttpClient) { }

  getReviews(): Observable<Review[]> {
    return this.http.get<Review[]>(this.baseUrl, { withCredentials: true } );
  }
}
