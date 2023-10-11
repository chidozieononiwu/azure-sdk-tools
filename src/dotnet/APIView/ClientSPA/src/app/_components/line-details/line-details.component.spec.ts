import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LineDetailsComponent } from './line-details.component';

describe('LineDetailsComponent', () => {
  let component: LineDetailsComponent;
  let fixture: ComponentFixture<LineDetailsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [LineDetailsComponent]
    });
    fixture = TestBed.createComponent(LineDetailsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
