import axios from 'axios'
import type { CompletedTranslationCompareResponse } from '@/ts/translationCompare'

export interface EditEntry {
    position: number
    translatedText: string
}

export const translationCompareService = {
    async getCompletedTranslationCompare(
        translationRequestId: number
    ): Promise<CompletedTranslationCompareResponse> {
        const response = await axios.get<CompletedTranslationCompareResponse>(
            `/api/translation-compare/${translationRequestId}`,
            {
                headers: {
                    'Cache-Control': 'no-cache',
                    Pragma: 'no-cache'
                },
                params: {
                    _: Date.now()
                }
            }
        )
        return response.data
    },

    async saveEdits(
        requestId: number,
        edits: EditEntry[],
        sourceFingerprint: string
    ): Promise<CompletedTranslationCompareResponse> {
        const response = await axios.post<CompletedTranslationCompareResponse>(
            `/api/translation-compare/${requestId}/save`,
            { sourceFingerprint, edits },
            { headers: { 'Cache-Control': 'no-cache', Pragma: 'no-cache' } }
        )
        return response.data
    },

    async acceptTranslation(
        requestId: number,
        edits: EditEntry[] | undefined,
        sourceFingerprint: string
    ): Promise<CompletedTranslationCompareResponse> {
        const response = await axios.post<CompletedTranslationCompareResponse>(
            `/api/translation-compare/${requestId}/accept`,
            { sourceFingerprint, edits },
            { headers: { 'Cache-Control': 'no-cache', Pragma: 'no-cache' } }
        )
        return response.data
    }
}
