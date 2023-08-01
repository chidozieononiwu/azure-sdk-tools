import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class ReviewsService {
  baseUrl : String = "http://localhost:3000/api/reviews/";

  constructor(private http: HttpClient) { }

  getReviews() {

  }
}
