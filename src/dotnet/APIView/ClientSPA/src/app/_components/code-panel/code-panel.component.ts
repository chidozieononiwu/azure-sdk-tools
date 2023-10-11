import {Component, Input, SimpleChanges } from '@angular/core';
import { ReviewLine, DiffLineKind} from 'src/app/_models/review';

@Component({
  selector: 'app-code-panel',
  templateUrl: './code-panel.component.html',
  styleUrls: ['./code-panel.component.scss'],
})

export class CodePanelComponent { 
  @Input() reviewLines: ReviewLine[] = [];
  lineNumberWidth: string = "2ch";


  ngOnChanges(changes: SimpleChanges) {
    if (changes['reviewLines']){
      this.lineNumberWidth = this.reviewLines[this.reviewLines.length - 1].codeLine.lineNumber.toString().length + "ch";
    }
  }
}
