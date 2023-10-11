import { Component, Input } from '@angular/core';
import { CodeLine } from 'src/app/_models/review';

@Component({
  selector: 'app-line-details',
  templateUrl: './line-details.component.html',
  styleUrls: ['./line-details.component.scss']
})
export class LineDetailsComponent {
  @Input() codeLine: CodeLine | undefined = undefined;
  @Input() lineNumberWidth: string | undefined = undefined;

}
