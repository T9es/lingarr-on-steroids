<template>
    <div class="p-4">
        <CardComponent title="Upload Workspace">
            <template #description>
                Configure the managed storage root, retention, and batch limits used by browser
                uploads.
            </template>

            <div class="grid gap-4 lg:grid-cols-2">
                <div class="lg:col-span-2">
                    <label class="text-primary-content/80 mb-1 block text-sm">Storage Root</label>
                    <div class="flex gap-2">
                        <input
                            v-model="settings.upload_workspace_storage_root"
                            class="bg-secondary border-accent/20 text-primary-content min-w-0 flex-1 rounded-md border px-3 py-2"
                            type="text" />
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2"
                            @click="isDirectoryModalOpen = true">
                            Browse
                        </button>
                    </div>
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">
                        Retention (days)
                    </label>
                    <input
                        v-model="settings.upload_workspace_retention_days"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                        type="number"
                        min="1" />
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">
                        Reserved Upload Worker Slots
                    </label>
                    <input
                        v-model="settings.upload_workspace_reserved_worker_slots"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                        type="number"
                        min="0" />
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">Max Batch Size</label>
                    <input
                        v-model="settings.upload_workspace_max_batch_size"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                        type="number"
                        min="1" />
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">
                        Max File Size (bytes)
                    </label>
                    <input
                        v-model="settings.upload_workspace_max_file_size_bytes"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                        type="number"
                        min="1" />
                </div>
            </div>

            <div class="mt-4 flex flex-wrap items-center gap-3">
                <button
                    class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-60"
                    :disabled="savingSettings"
                    @click="saveSettings">
                    {{ savingSettings ? 'Saving...' : 'Save Workspace Settings' }}
                </button>
                <span v-if="successMessage" class="text-sm text-green-500">
                    {{ successMessage }}
                </span>
                <span v-if="errorMessage" class="text-sm text-red-500">
                    {{ errorMessage }}
                </span>
            </div>
        </CardComponent>

        <DirectoryModal
            :is-open="isDirectoryModalOpen"
            @close="isDirectoryModalOpen = false"
            @select="handleDirectorySelection" />
    </div>
</template>

<script setup lang="ts">
import axios from 'axios'
import { onMounted, ref } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import DirectoryModal from '@/components/features/settings/DirectoryModal.vue'
import services from '@/services'

type UploadWorkspaceSettings = Record<UploadWorkspaceSettingKey, string>

type UploadWorkspaceSettingKey =
    | 'upload_workspace_storage_root'
    | 'upload_workspace_retention_days'
    | 'upload_workspace_reserved_worker_slots'
    | 'upload_workspace_max_batch_size'
    | 'upload_workspace_max_file_size_bytes'

type ApiErrorBody = {
    title?: string
    message?: string
    detail?: string
    errors?: Record<string, string[]>
}

type ApiErrorResponse = {
    data?: string | ApiErrorBody
    statusText?: string
}

const settingKeys: UploadWorkspaceSettingKey[] = [
    'upload_workspace_storage_root',
    'upload_workspace_retention_days',
    'upload_workspace_reserved_worker_slots',
    'upload_workspace_max_batch_size',
    'upload_workspace_max_file_size_bytes'
]

const defaultSettings = (): UploadWorkspaceSettings => ({
    upload_workspace_storage_root: '',
    upload_workspace_retention_days: '30',
    upload_workspace_reserved_worker_slots: '1',
    upload_workspace_max_batch_size: '50',
    upload_workspace_max_file_size_bytes: '2147483648'
})

const settings = ref<UploadWorkspaceSettings>(defaultSettings())
const savingSettings = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const isDirectoryModalOpen = ref(false)

const setMessage = (message: string) => {
    successMessage.value = message
    errorMessage.value = ''
}

const setError = (message: string) => {
    errorMessage.value = message
    successMessage.value = ''
}

const messageFromResponseData = (responseDataRaw: unknown): string => {
    if (typeof responseDataRaw === 'string' && responseDataRaw.trim()) {
        return responseDataRaw
    }

    const responseData = responseDataRaw as ApiErrorBody | undefined
    if (responseData?.message) {
        return responseData.message
    }
    if (responseData?.title) {
        return responseData.title
    }
    if (responseData?.detail) {
        return responseData.detail
    }
    if (responseData?.errors) {
        const firstKey = Object.keys(responseData.errors)[0]
        const firstMessage = responseData.errors[firstKey]?.[0]
        if (firstMessage) {
            return firstMessage
        }
    }

    return ''
}

const toErrorMessage = (error: unknown, fallback: string): string => {
    if (axios.isAxiosError(error)) {
        const responseMessage = messageFromResponseData(error.response?.data)
        if (responseMessage) {
            return responseMessage
        }
        if (error.response?.statusText) {
            return error.response.statusText
        }
        if (error.code === 'ERR_NETWORK') {
            return 'Network error. Check your connection and try again.'
        }
        if (error.code === 'ECONNABORTED') {
            return 'The request timed out before the server responded.'
        }
    }

    const response = error as ApiErrorResponse | undefined
    const responseMessage = messageFromResponseData(response?.data)
    if (responseMessage) {
        return responseMessage
    }
    if (response?.statusText) {
        return response.statusText
    }

    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
}

const loadSettings = async () => {
    try {
        const loadedSettings =
            await services.setting.getSettings<Partial<UploadWorkspaceSettings>>(settingKeys)
        settings.value = {
            ...defaultSettings(),
            ...loadedSettings
        }
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to load upload workspace settings.'))
    }
}

const saveSettings = async () => {
    savingSettings.value = true
    successMessage.value = ''
    errorMessage.value = ''

    try {
        await Promise.all(
            Object.entries(settings.value).map(([key, value]) =>
                services.setting.setSetting(key, value)
            )
        )
        setMessage('Upload workspace settings saved.')
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to save upload workspace settings.'))
    } finally {
        savingSettings.value = false
    }
}

const handleDirectorySelection = (path: string) => {
    settings.value.upload_workspace_storage_root = path
    isDirectoryModalOpen.value = false
}

onMounted(loadSettings)
</script>
