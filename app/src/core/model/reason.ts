// Port of DraftPilot.Core.Draft.Reason (1.5): one line of explanation with a tone and an optional
// hover text that explains the term in plain words.

export type ReasonTone = 'neutral' | 'pro' | 'contra';

export interface Reason {
  text: string;
  tone: ReasonTone;
  hint: string | null;
}

export const pro = (text: string, hint: string | null = null): Reason => ({ text, tone: 'pro', hint });
export const contra = (text: string, hint: string | null = null): Reason => ({ text, tone: 'contra', hint });
export const neutral = (text: string, hint: string | null = null): Reason => ({ text, tone: 'neutral', hint });
