import { Component } from '@angular/core';
import { Review } from 'src/app/_models/review';
import { AuthService } from 'src/app/_services/auth/auth.service';
import { ReviewsService } from 'src/app/_services/reviews/reviews.service';

@Component({
  selector: 'app-index-page',
  templateUrl: './index-page.component.html',
  styleUrls: ['./index-page.component.scss']
})
export class IndexPageComponent {
  reviews: any

  constructor(private reviewsService: ReviewsService, private authService : AuthService) { }

  ngOnInit() {
    this.reviewsService.getReviews().subscribe({
        next : (reviews: Review[]) => {
          this.reviews = reviews;
        },
        error : (error: any) => console.log(error)
    });
  }

}
