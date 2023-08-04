import { Component } from '@angular/core';
import { Review } from 'src/app/_models/review';
import { ReviewsService } from 'src/app/_services/reviews/reviews.service';

@Component({
  selector: 'app-index-page',
  templateUrl: './index-page.component.html',
  styleUrls: ['./index-page.component.scss']
})
export class IndexPageComponent {
  reviews: any

  constructor(private reviewsService: ReviewsService) { }

  ngOnInit() {
    this.reviewsService.getReviews().subscribe((reviews: Review[]) => {
      this.reviews = reviews;
    });
  }

}
