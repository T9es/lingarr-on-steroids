<template>
    <div class="space-y-6 p-6">
        <CardComponent :title="translate('help.about.title')">
            <template #description>
                {{ translate('help.about.description') }}
            </template>
            <template #actions>
                <div class="flex flex-wrap items-center gap-2">
                    <button
                        @click="checkForUpdates"
                        :disabled="checkingUpdates"
                        class="bg-accent text-primary-content hover:bg-accent/80 disabled:bg-secondary disabled:text-primary-content/50 flex items-center gap-2 rounded px-4 py-2 text-sm font-semibold transition-colors disabled:cursor-not-allowed">
                        <LoaderCircleIcon v-if="checkingUpdates" class="h-4 w-4 animate-spin" />
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
                        class="border-secondary bg-secondary hover:bg-secondary/80 text-primary-content flex items-center gap-2 rounded border px-4 py-2 text-sm font-semibold transition-colors">
                        <GithubIcon class="h-4 w-4" />
                        <span>{{ translate('help.about.viewOnGithub') }}</span>
                    </a>
                </div>
            </template>
            <template #content>
                <div class="space-y-4">
                    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                        <div class="bg-secondary/30 rounded-md p-4">
                            <div class="text-secondary-content text-sm">Current Build</div>
                            <div class="text-primary-content mt-2 break-all text-lg font-semibold">
                                {{ displayVersion }}
                            </div>
                        </div>
                        <div class="bg-secondary/30 rounded-md p-4">
                            <div class="text-secondary-content text-sm">
                                {{ translate('help.about.version') }}
                            </div>
                            <div class="text-primary-content mt-2 text-lg font-semibold">
                                {{ currentVersion }}
                            </div>
                        </div>
                        <div class="bg-secondary/30 rounded-md p-4">
                            <div class="text-secondary-content text-sm">Latest Release</div>
                            <div class="text-primary-content mt-2 text-lg font-semibold">
                                {{ latestRelease }}
                            </div>
                        </div>
                        <div class="bg-secondary/30 rounded-md p-4">
                            <div class="text-secondary-content text-sm">Update Status</div>
                            <div class="mt-2">
                                <span class="rounded px-2 py-1 text-sm font-medium" :class="statusBadgeClasses">
                                    {{ updateStatusLabel }}
                                </span>
                            </div>
                        </div>
                    </div>

                    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
                        <div class="bg-secondary/20 space-y-3 rounded-md p-4">
                            <h3 class="text-primary-content text-base font-semibold">
                                Build Details
                            </h3>
                            <div class="grid gap-3 sm:grid-cols-2">
                                <div>
                                    <div class="text-secondary-content text-xs uppercase">
                                        Branch
                                    </div>
                                    <div class="text-primary-content mt-1 font-medium">
                                        {{ version.branchName || 'release' }}
                                    </div>
                                </div>
                                <div>
                                    <div class="text-secondary-content text-xs uppercase">
                                        Commit
                                    </div>
                                    <div class="text-primary-content mt-1 break-all font-mono text-sm">
                                        {{ version.commitSha || 'tagged release' }}
                                    </div>
                                </div>
                                <div>
                                    <div class="text-secondary-content text-xs uppercase">
                                        Base Tag
                                    </div>
                                    <div class="text-primary-content mt-1 font-medium">
                                        {{ version.baseTag || currentVersion }}
                                    </div>
                                </div>
                                <div>
                                    <div class="text-secondary-content text-xs uppercase">
                                        Build Type
                                    </div>
                                    <div class="text-primary-content mt-1 font-medium">
                                        {{
                                            version.isDevBuild
                                                ? translate('help.about.devBuild')
                                                : 'Release'
                                        }}
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div class="bg-secondary/20 space-y-3 rounded-md p-4">
                            <h3 class="text-primary-content text-base font-semibold">
                                Update Check
                            </h3>
                            <p class="text-secondary-content text-sm leading-6">
                                Update checks compare this build against the latest GitHub release.
                            </p>
                            <div
                                v-if="commitsPastRelease"
                                class="border-secondary bg-primary rounded-md border px-3 py-2 text-sm">
                                {{ commitsPastRelease }}
                            </div>
                            <div v-if="version.newVersion" class="text-sm text-green-400">
                                {{ translate('help.about.updateAvailable') }}
                            </div>
                        </div>
                    </div>
                </div>
            </template>
        </CardComponent>

        <CardComponent title="Documentation">
            <template v-if="loading" #content>
                <div class="flex items-center gap-3 py-2">
                    <LoaderCircleIcon class="h-5 w-5 animate-spin" />
                    <span class="text-secondary-content">
                        {{ translate('help.about.loadingDocs') }}
                    </span>
                </div>
            </template>

            <template v-else-if="error" #content>
                <div class="rounded border border-red-500/30 bg-red-500/10 p-4 text-sm text-red-300">
                    {{ translate('help.about.failedToLoad') }}{{ error }}
                </div>
            </template>

            <template v-else #content>
                <div class="space-y-6">
                    <section v-if="overviewHtml" class="space-y-3">
                        <h3 class="text-primary-content text-base font-semibold">Overview</h3>
                        <div class="about-markdown" v-html="overviewHtml"></div>
                    </section>

                    <section
                        v-for="section in readmeSections"
                        :key="section.id"
                        :id="section.id"
                        class="border-secondary/60 space-y-3 border-t pt-6 first:border-t-0 first:pt-0">
                        <h3 class="text-primary-content text-base font-semibold">
                            {{ section.title }}
                        </h3>
                        <div class="about-markdown" v-html="section.html"></div>
                    </section>
                </div>
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { marked } from 'marked'
import CardComponent from '@/components/common/CardComponent.vue'
import GithubIcon from '@/components/icons/GithubIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useInstanceStore } from '@/store/instance'
import { useI18n } from '@/plugins/i18n'

interface ReadmeSection {
    id: string
    title: string
    html: string
}

const { translate, locale } = useI18n()

marked.setOptions({
    gfm: true,
    breaks: true
})

const instanceStore = useInstanceStore()
const version = computed(() => instanceStore.version)
const currentVersion = computed(() => instanceStore.version.currentVersion || 'Unknown')
const displayVersion = computed(
    () => instanceStore.version.displayVersion || instanceStore.version.currentVersion || 'Unknown'
)
const latestRelease = computed(
    () => instanceStore.version.latestVersion || instanceStore.version.currentVersion || 'Unknown'
)
const checkingUpdates = ref(false)

const loading = ref(true)
const error = ref<string | null>(null)
const overviewHtml = ref('')
const readmeSections = ref<ReadmeSection[]>([])

const commitsPastRelease = computed(() => {
    const count = version.value.commitsSinceTag
    const baseVersion = version.value.baseTag || currentVersion.value

    if (!count || count <= 0) {
        return ''
    }

    return `${count} commits past ${baseVersion}`
})

const updateStatusLabel = computed(() => {
    if (version.value.newVersion) {
        return translate('help.about.updateAvailable')
    }

    if (version.value.isDevBuild) {
        return 'Ahead of latest release'
    }

    return 'Up to date'
})

const statusBadgeClasses = computed(() => {
    if (version.value.newVersion) {
        return 'bg-green-500/20 text-green-400'
    }

    if (version.value.isDevBuild) {
        return 'bg-blue-500/20 text-blue-300'
    }

    return 'bg-secondary text-primary-content'
})

function transformLinks(content: string): string {
    return content
        .replace(/\]\(\./g, '](https://github.com/T9es/lingarr-on-steroids/blob/main/.')
        .replace(
            /\]\(LICENSE\)/g,
            '](https://github.com/T9es/lingarr-on-steroids/blob/main/LICENSE)'
        )
}

function slugify(text: string): string {
    return text
        .normalize('NFKD')
        .toLowerCase()
        .trim()
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/[^\p{Letter}\p{Number}\s-]/gu, '')
        .replace(/\s+/g, '-')
        .replace(/-+/g, '-')
}

function isBadgeRow(node: Element): boolean {
    if (node.tagName !== 'P') {
        return false
    }

    const links = Array.from(node.querySelectorAll('a'))
    const images = Array.from(node.querySelectorAll('img'))

    return links.length > 0 && images.length > 0 && images.length === links.length
}

function isLanguageRow(node: Element): boolean {
    if (node.tagName !== 'P') {
        return false
    }

    const links = Array.from(node.querySelectorAll('a'))
    const images = Array.from(node.querySelectorAll('img'))

    return links.length >= 3 && images.length === 0
}

function buildReadmeSections(html: string) {
    const parser = new DOMParser()
    const documentRoot = parser.parseFromString(html, 'text/html')
    const children = Array.from(documentRoot.body.children)
    const nextSections: ReadmeSection[] = []
    const overviewNodes: string[] = []
    const seenIds = new Set<string>()

    let currentSection: { id: string; title: string; nodes: string[] } | null = null

    const commitSection = () => {
        if (!currentSection || currentSection.nodes.length === 0) {
            return
        }

        nextSections.push({
            id: currentSection.id,
            title: currentSection.title,
            html: currentSection.nodes.join('')
        })
    }

    for (const node of children) {
        if (node.tagName === 'H1' || node.tagName === 'HR' || isBadgeRow(node) || isLanguageRow(node)) {
            continue
        }

        if (node.tagName === 'H2') {
            commitSection()

            const title = node.textContent?.trim() || 'Documentation'
            let id = slugify(title)
            let suffix = 2

            while (seenIds.has(id)) {
                id = `${slugify(title)}-${suffix}`
                suffix += 1
            }

            seenIds.add(id)
            currentSection = {
                id,
                title,
                nodes: []
            }

            continue
        }

        if (currentSection) {
            currentSection.nodes.push(node.outerHTML)
        } else {
            overviewNodes.push(node.outerHTML)
        }
    }

    commitSection()

    overviewHtml.value = overviewNodes.join('')
    readmeSections.value = nextSections
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
        const html = (await marked.parse(transformedContent)) as string
        buildReadmeSections(html)
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

watch(
    () => locale.value,
    () => {
        fetchReadme()
    },
    { immediate: true }
)
</script>

<style scoped>
.about-markdown {
    color: var(--secondary-content);
}

.about-markdown :deep(p),
.about-markdown :deep(ul),
.about-markdown :deep(ol),
.about-markdown :deep(blockquote),
.about-markdown :deep(table),
.about-markdown :deep(pre) {
    margin-top: 0.9rem;
    margin-bottom: 0.9rem;
}

.about-markdown :deep(p),
.about-markdown :deep(li) {
    line-height: 1.75;
}

.about-markdown :deep(strong) {
    color: var(--primary-content);
}

.about-markdown :deep(a) {
    color: var(--accent);
    text-decoration: none;
}

.about-markdown :deep(a:hover) {
    text-decoration: underline;
}

.about-markdown :deep(blockquote) {
    border-left: 3px solid color-mix(in srgb, var(--accent) 45%, transparent);
    background: color-mix(in srgb, var(--secondary) 45%, transparent);
    border-radius: 0.375rem;
    padding: 0.85rem 1rem;
    color: var(--primary-content);
}

.about-markdown :deep(code) {
    border-radius: 0.25rem;
    background: color-mix(in srgb, var(--secondary) 75%, transparent);
    padding: 0.15rem 0.35rem;
    color: var(--primary-content);
    font-size: 0.92em;
}

.about-markdown :deep(pre) {
    overflow-x: auto;
    border: 1px solid color-mix(in srgb, var(--secondary-content) 12%, transparent);
    border-radius: 0.375rem;
    background: color-mix(in srgb, var(--secondary) 80%, transparent);
    padding: 1rem;
}

.about-markdown :deep(pre code) {
    background: transparent;
    padding: 0;
}

.about-markdown :deep(table) {
    display: block;
    width: 100%;
    overflow-x: auto;
    border-collapse: separate;
    border-spacing: 0;
    border: 1px solid color-mix(in srgb, var(--secondary-content) 12%, transparent);
    border-radius: 0.375rem;
}

.about-markdown :deep(th),
.about-markdown :deep(td) {
    min-width: 12rem;
    border-bottom: 1px solid color-mix(in srgb, var(--secondary-content) 10%, transparent);
    padding: 0.85rem 1rem;
    text-align: left;
    vertical-align: top;
}

.about-markdown :deep(th) {
    background: color-mix(in srgb, var(--secondary) 70%, transparent);
    color: var(--primary-content);
    font-weight: 700;
}

.about-markdown :deep(tr:last-child td) {
    border-bottom: none;
}

.about-markdown :deep(ul),
.about-markdown :deep(ol) {
    padding-left: 1.25rem;
}
</style>
