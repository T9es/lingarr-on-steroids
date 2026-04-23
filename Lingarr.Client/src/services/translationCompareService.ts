import axios from 'axios'
import type { CompletedTranslationCompareResponse } from '@/ts/translationCompare'

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
    }
}
