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
                        <span class="text-secondary-content">
                            {{ translate('help.about.version') }}
                        </span>
                        <span class="text-primary-content text-lg font-semibold">
                            {{ currentVersion }}
                        </span>
                        <span
                            v-if="version.isDevBuild"
                            class="rounded-full border border-orange-500/30 bg-orange-500/20 px-2.5 py-0.5 text-xs font-medium text-orange-400">
                            {{ translate('help.about.devBuild') }}
                        </span>
                        <span
                            v-if="version.newVersion"
                            class="rounded-full border border-green-500/30 bg-green-500/20 px-2.5 py-0.5 text-xs font-medium text-green-400">
                            {{ translate('help.about.updateAvailable') }}
                        </span>
                    </div>
                    <div class="flex items-center gap-3">
<button
                            @click="checkForUpdates"
                            :disabled="checkingUpdates"
                            class="bg-accent text-primary-content flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-opacity hover:opacity-90 disabled:opacity-50">
                            <LoaderCircleIcon
                                v-if="checkingUpdates"
                                class="h-4 w-4 animate-spin" />
                            {{
                                checkingUpdates
                                    ? translate('common.loading')
                                    : translate('help.about.checkUpdates')
                            }}
                        </button>
                        <a
                            href="https://github.com/T9es/lingarr-on-steroids"
                            target="_blank"
                            rel="noopener noreferrer"
                            class="bg-primary/50 text-primary-content hover:bg-primary/70 border-accent/30 flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm font-medium transition-colors">
                            <GithubIcon class="h-4 w-4" />
                            <span>{{ translate('help.about.viewOnGithub') }}</span>
                        </a>
                    </div>
                </div>
            </template>
        </CardComponent>

        <!-- README Content -->
        <div class="bg-secondary/30 rounded-md p-6 shadow-md">
            <div v-if="loading" class="flex items-center gap-2 py-8">
                <LoaderCircleIcon class="h-5 w-5 animate-spin" />
                <span class="text-secondary-content">
                    {{ translate('help.about.loadingDocs') }}
                </span>
            </div>
            <div
                v-else-if="error"
                class="rounded-md border border-red-500/30 bg-red-500/10 p-4 text-red-400">
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
        .replace(
            /\]\(LICENSE\)/g,
            '](https://github.com/T9es/lingarr-on-steroids/blob/main/LICENSE)'
        )
        .replace(
            /\]\(LICENSE\)/g,
            '](https://github.com/T9es/lingarr-on-steroids/blob/main/LICENSE)'
        )
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
    const startTime = Date.now()
    try {
        await instanceStore.applyVersionOnLoad()
    } finally {
        const elapsed = Date.now() - startTime
        const remaining = 500 - elapsed
        if (remaining > 0) {
            await new Promise((resolve) => setTimeout(resolve, remaining))
        }
        checkingUpdates.value = false
    }
}

onMounted(() => {
    fetchReadme()
})
</script>
