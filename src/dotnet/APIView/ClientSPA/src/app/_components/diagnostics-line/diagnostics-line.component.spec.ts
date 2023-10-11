import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DiagnosticsLineComponent } from './diagnostics-line.component';

describe('DiagnosticsLineComponent', () => {
  let component: DiagnosticsLineComponent;
  let fixture: ComponentFixture<DiagnosticsLineComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [DiagnosticsLineComponent]
    });
    fixture = TestBed.createComponent(DiagnosticsLineComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
