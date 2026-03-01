<template>
    <div class="space-y-6 p-6">
        <!-- Version Header Card -->
        <CardComponent :title="translate('help.about.title')">
            <template #description>
                {{ translate('help.about.description') }}
            </template>
            <template #content>
                <div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                    <div class="flex items-center gap-3">
                        <span class="text-secondary-content">{{ translate('help.about.version') }}</span>
                        <span class="text-lg font-semibold text-primary-content">{{ currentVersion }}</span>
                        <span
                            v-if="version.isDevBuild"
                            class="rounded-full bg-orange-500/20 px-2.5 py-0.5 text-xs font-medium text-orange-400 border border-orange-500/30">
                            Dev Build
                        </span>
                        <span
                            v-if="version.newVersion"
                            class="rounded-full bg-green-500/20 px-2.5 py-0.5 text-xs font-medium text-green-400 border border-green-500/30">
                            Update Available
                        </span>
                    </div>
                    <div class="flex items-center gap-3">
                        <button
                            @click="checkForUpdates"
                            :disabled="checkingUpdates"
                            class="rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-primary-content transition-opacity hover:opacity-90 disabled:opacity-50">
                            {{ checkingUpdates ? translate('common.loading') : translate('help.about.checkUpdates') }}
                        </button>
                        <a
                            href="https://github.com/T9es/lingarr-on-steroids"
                            target="_blank"
                            rel="noopener noreferrer"
                            class="flex items-center gap-2 rounded-md bg-primary/50 px-3 py-1.5 text-sm font-medium text-primary-content transition-colors hover:bg-primary/70 border border-accent/30">
                            <GithubIcon class="h-4 w-4" />
                            <span>{{ translate('help.about.viewOnGithub') }}</span>
                        </a>
                    </div>
                </div>
            </template>
        </CardComponent>

        <!-- README Content -->
        <div class="rounded-md bg-secondary/30 p-6 shadow-md">
            <div v-if="loading" class="flex items-center gap-2 py-8">
                <LoaderCircleIcon class="h-5 w-5 animate-spin" />
                <span class="text-secondary-content">{{ translate('help.about.loadingDocs') }}</span>
            </div>
            <div v-else-if="error" class="rounded-md border border-red-500/30 bg-red-500/10 p-4 text-red-400">
                {{ translate('help.about.failedToLoad') }}{{ error }}
            </div>
            <div
                v-else
                class="prose prose-invert prose-headings:text-primary-content prose-p:text-secondary-content prose-a:text-accent prose-strong:text-primary-content prose-code:text-primary-content prose-pre:bg-primary prose-th:border-accent prose-th:bg-primary/50 prose-th:text-primary-content prose-td:border-accent/50 prose-td:text-secondary-content prose-li:text-secondary-content prose-ul:text-secondary-content prose-ol:text-secondary-content max-w-none"
                v-html="readmeContent"></div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { marked } from 'marked'
import CardComponent from '@/components/common/CardComponent.vue'
import GithubIcon from '@/components/icons/GithubIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useInstanceStore } from '@/store/instance'
import { useI18n } from '@/plugins/i18n'

const { translate, locale } = useI18n()

marked.setOptions({
    gfm: true,
    breaks: true
})

const instanceStore = useInstanceStore()
const currentVersion = computed(() => instanceStore.version.currentVersion || 'Unknown')
const version = computed(() => instanceStore.version)
const checkingUpdates = ref(false)

const loading = ref(true)
const error = ref<string | null>(null)
const readmeContent = ref('')

function transformLinks(content: string): string {
    return content
        .replace(/\]\(\./g, '](https://github.com/T9es/lingarr-on-steroids/blob/main/.')
        .replace(/\]\(LICENSE\)/g, '](https://github.com/T9es/lingarr-on-steroids/blob/main/LICENSE)')
        .replace(/\]\(LICENSE\)/g, '](https://github.com/T9es/lingarr-on-steroids/blob/main/LICENSE)')
}

async function fetchReadme() {
    try {
        loading.value = true
        error.value = null

        const lang = locale.value || 'en'
        const response = await fetch(`/api/Version/readme?lang=${lang}`)

        if (!response.ok) {
            if (response.status === 404) {
                throw new Error('README file not found')
            }
            throw new Error(`HTTP ${response.status}: ${response.statusText}`)
        }

        const content = await response.text()
        const transformedContent = transformLinks(content)
        readmeContent.value = (await marked.parse(transformedContent)) as string
    } catch (err) {
        error.value = err instanceof Error ? err.message : 'Unknown error occurred'
        console.error('Failed to fetch README:', err)
    } finally {
        loading.value = false
    }
}

async function checkForUpdates() {
    checkingUpdates.value = true
    try {
        await instanceStore.applyVersionOnLoad()
    } finally {
        checkingUpdates.value = false
    }
}

onMounted(() => {
    fetchReadme()
})
</script>