import { CommentType } from "./commentItemModel";

export class CreateCommentModel
{
    reviewId: string = '';
    elementId: string = '';
    commentText: string = '';
    commentType: CommentType | null = null;
    apiRevisionId: string = '';
    sampleRevisionId: string = '';
    resolutionLocked: boolean = false;
}

export class UpdateCommentModel
{
    reviewId: string = '';
    commentId: string = '';
    commentText: string = '';
}