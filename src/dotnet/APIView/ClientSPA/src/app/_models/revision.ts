import { CodeDiagnostic, CommentItemModel } from "./review"
import { JsonObject, JsonProperty } from 'json2typescript';

export enum ReviewPageWorkerMessageDirective {
  CreatePageNavigation,
  UpdateCodePanelData,
  UpdateCodePanelRowData,
}

export enum CodePanelRowDatatype {
  CodeLine = "CodeLine",
  Documentation = "Documentation",
  Diagnostics = "Diagnostics",
  CommentThread = "CommentThread"
}

export interface APIRevision {
  id: string
  reviewId: string
  packageName: string
  language: string
  apiRevisionType: string
  pullRequestNo: number
  label: string
  resolvedLabel: string
  packageVersion: string
  assignedReviewers: AssignedReviewer[]
  isApproved: boolean
  createdBy: string
  createdOn: string
  lastUpdatedOn: string
  isReleased: boolean,
  releasedOn: string,
  isDeleted: boolean
}


export interface AssignedReviewer {
  assignedBy: string;
  assingedTo: string;
  assingedOn: string;
}

export interface StructuredToken {
  value: string;
  id: string;
  kind: string;
  properties: { [key: string]: string; }
  renderClasses: Set<string>
}

export interface DiffLineInProcess {
  groupId: string | undefined;
  lineTokens: StructuredToken[];
  tokenIdsInLine: Set<string>;
}

export interface APITreeNode {
  name: string;
  id: string;
  kind: string;
  tags: Set<string>
  properties: { [key: string]: string; }
  topTokens: StructuredToken[];
  bottomTokens: StructuredToken[];
  topDiffTokens: StructuredToken[];
  bottomDiffTokens: StructuredToken[];
  children: APITreeNode[];
  diffKind: string;
}

@JsonObject('cprd')
export class CodePanelRowData {
  @JsonProperty('t')
  type: CodePanelRowDatatype = CodePanelRowDatatype.CodeLine
  @JsonProperty('ln')
  lineNumber?: number = 0
  @JsonProperty('rot')
  rowOfTokens?: StructuredToken[] = []
  @JsonProperty('ni')
  nodeId: string = ''
  @JsonProperty('nih')
  nodeIdHashed: string = ''
  @JsonProperty('rotp')
  rowOfTokensPosition: string  = ''
  @JsonProperty('rc')
  rowClasses: Set<string> = new Set<string>()
  @JsonProperty('i')
  indent?: number = 0
  @JsonProperty('dk')
  diffKind?: string = ''
  @JsonProperty('rs')
  rowSize: number = 0
  @JsonProperty('tdc')
  toggleDocumentationClasses?: string = ''
  @JsonProperty('tcc')
  toggleCommentsClasses?: string = ''
  @JsonProperty('d')
  diagnostics?: CodeDiagnostic = undefined;
  @JsonProperty('c')
  comments?: CommentItemModel[] = []
}

@JsonObject('cprmd')
export class CodePanelNodeMetaData {
  @JsonProperty('doc')
  documentation: CodePanelRowData[] = [];
  @JsonProperty('d')
  diagnostics: CodePanelRowData[] =[];
  @JsonProperty('cl')
  codeLines: CodePanelRowData[] = [];
  @JsonProperty('ct')
  commentThread: CodePanelRowData[] = [];
  @JsonProperty('ntn')
  navigationTreeNode: NavigationTreeNode = new NavigationTreeNode();
  @JsonProperty('pnih')
  parentNodeIdHashed: string = '';
  @JsonProperty('cniio')
  childrenNodeIdsInOrder: { [key: number]: string } = {};
  @JsonProperty('inwd')
  isNodeWithDiff: boolean = false;
  @JsonProperty('inwdid')
  isNodeWithDiffInDescendants: boolean = false;
  @JsonProperty('btnih')
  bottomTokenNodeIdHash: string = '';
}

@JsonObject('cpd')
export class CodePanelData {
  @JsonProperty('nmd')
  nodeMetaData: { [key: string]: CodePanelNodeMetaData } = {}
}

@JsonObject('ntnd')
export class NavigationTreeNodeData {
  @JsonProperty('k')
  kind?: string = '';
  @JsonProperty('i')
  icon?: string = '';
}

@JsonObject('ntnd')
export class NavigationTreeNode {
  @JsonProperty('l')
  label: string = '';
  @JsonProperty('d')
  data?: NavigationTreeNodeData = undefined;
  @JsonProperty('r')
  expanded: boolean = false;
  @JsonProperty('c')
  children: NavigationTreeNode [] = [];
}

export interface InsertCodePanelRowDataMessage {
  directive: ReviewPageWorkerMessageDirective
  payload : any
}

export interface ApiTreeBuilderData {
  diffStyle: string,
  showDocumentation: boolean, 
}

export interface CodePanelToggleableData {
  documentation: CodePanelRowData[]
  diagnostics: CodePanelRowData[]
  comments: CodePanelRowData[]
}