import { Component, Input, SimpleChanges } from '@angular/core';
import { CodeDiagnostics } from 'src/app/_models/review';

@Component({
  selector: 'app-diagnostics-line',
  templateUrl: './diagnostics-line.component.html',
  styleUrls: ['./diagnostics-line.component.scss']
})
export class DiagnosticsLineComponent {
  @Input() diagnosticsLine: CodeDiagnostics[] = [];
  errorDiagnostics: CodeDiagnostics[] = [];
  warningDiagnostics: CodeDiagnostics[] = [];
  infoDiagnostics: CodeDiagnostics [] = [];

  ngOnChanges(changes: SimpleChanges) {
    if (changes['diagnosticsLine'].currentValue){
      this.errorDiagnostics = this.diagnosticsLine!.filter(d => d.level === "Error" || d.level === "Default");
      this.warningDiagnostics = this.diagnosticsLine!.filter(d => d.level === "Warning");
      this.infoDiagnostics = this.diagnosticsLine!.filter(d => d.level === "Info");
    }
  }

}
