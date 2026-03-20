<template>
    <div class="space-y-6 p-6">
        <section
            class="from-primary/60 via-secondary/95 to-tertiary border-accent/20 overflow-hidden rounded-2xl border bg-linear-to-br p-6 shadow-lg">
            <div
                class="flex flex-col gap-6 border-b border-white/6 pb-6 xl:flex-row xl:items-start xl:justify-between">
                <div class="max-w-3xl space-y-4">
                    <div class="flex flex-wrap items-center gap-2">
                        <span
                            class="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs font-semibold tracking-[0.24em] uppercase text-primary-content/65">
                            {{ translate('help.about.title') }}
                        </span>
                        <span
                            class="rounded-full border px-3 py-1 text-xs font-medium"
                            :class="statusBadgeClasses">
                            {{ updateStatusLabel }}
                        </span>
                        <span
                            v-if="version.isDevBuild"
                            class="rounded-full border border-orange-500/30 bg-orange-500/15 px-3 py-1 text-xs font-medium text-orange-300">
                            {{ translate('help.about.devBuild') }}
                        </span>
                    </div>

                    <div class="space-y-3">
                        <h1 class="text-3xl leading-tight font-semibold text-primary-content md:text-4xl">
                            Lingarr on Steroids
                        </h1>
                        <p class="max-w-2xl text-base leading-7 text-primary-content/72 md:text-lg">
                            {{ translate('help.about.description') }}
                        </p>
                    </div>

                    <div
                        v-if="commitsPastRelease || version.branchName || version.commitSha"
                        class="border-accent/20 bg-primary/35 flex flex-wrap items-center gap-3 rounded-xl border px-4 py-3 text-sm text-primary-content/75">
                        <span v-if="version.branchName" class="font-medium text-primary-content">
                            {{ version.branchName }}
                        </span>
                        <span v-if="version.commitSha" class="font-mono text-primary-content/80">
                            {{ version.commitSha }}
                        </span>
                        <span v-if="commitsPastRelease">
                            {{ commitsPastRelease }}
                        </span>
                    </div>
                </div>

                <div class="flex flex-wrap items-center gap-3 xl:justify-end">
                    <button
                        @click="checkForUpdates"
                        :disabled="checkingUpdates"
                        class="bg-accent text-primary-content flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold transition-opacity hover:opacity-90 disabled:opacity-50">
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
                        class="border-accent/30 bg-primary/45 text-primary-content hover:bg-primary/65 flex items-center gap-2 rounded-xl border px-4 py-2.5 text-sm font-semibold transition-colors">
                        <GithubIcon class="h-4 w-4" />
                        <span>{{ translate('help.about.viewOnGithub') }}</span>
                    </a>
                </div>
            </div>

            <div class="mt-6 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <div
                    class="border-accent/15 bg-primary/35 rounded-xl border p-4 shadow-sm">
                    <div class="text-primary-content/55 text-xs font-semibold tracking-[0.18em] uppercase">
                        Current Build
                    </div>
                    <div class="mt-2 break-all text-lg font-semibold text-primary-content">
                        {{ displayVersion }}
                    </div>
                </div>
                <div
                    class="border-accent/15 bg-primary/35 rounded-xl border p-4 shadow-sm">
                    <div class="text-primary-content/55 text-xs font-semibold tracking-[0.18em] uppercase">
                        {{ translate('help.about.version') }}
                    </div>
                    <div class="mt-2 text-lg font-semibold text-primary-content">
                        {{ currentVersion }}
                    </div>
                </div>
                <div
                    class="border-accent/15 bg-primary/35 rounded-xl border p-4 shadow-sm">
                    <div class="text-primary-content/55 text-xs font-semibold tracking-[0.18em] uppercase">
                        Latest Release
                    </div>
                    <div class="mt-2 text-lg font-semibold text-primary-content">
                        {{ latestRelease }}
                    </div>
                </div>
                <div
                    class="border-accent/15 bg-primary/35 rounded-xl border p-4 shadow-sm">
                    <div class="text-primary-content/55 text-xs font-semibold tracking-[0.18em] uppercase">
                        Update Status
                    </div>
                    <div class="mt-2 text-lg font-semibold text-primary-content">
                        {{ updateStatusLabel }}
                    </div>
                </div>
            </div>

            <div class="mt-4 flex flex-wrap gap-5 text-sm text-primary-content/62">
                <div v-if="version.branchName" class="flex items-center gap-2">
                    <span class="font-medium text-primary-content/80">
                        Branch
                    </span>
                    <span>{{ version.branchName }}</span>
                </div>
                <div v-if="version.commitSha" class="flex items-center gap-2">
                    <span class="font-medium text-primary-content/80">
                        Commit
                    </span>
                    <span class="font-mono">{{ version.commitSha }}</span>
                </div>
            </div>

            <p class="mt-4 max-w-3xl text-sm leading-6 text-primary-content/58">
                Update checks compare this build against the latest GitHub release.
            </p>
        </section>

        <div v-if="loading" class="bg-primary/35 rounded-2xl border border-white/6 p-8 shadow-md">
            <div class="flex items-center gap-3">
                <LoaderCircleIcon class="h-5 w-5 animate-spin" />
                <span class="text-secondary-content">
                    {{ translate('help.about.loadingDocs') }}
                </span>
            </div>
        </div>

        <div
            v-else-if="error"
            class="rounded-2xl border border-red-500/30 bg-red-500/10 p-5 text-red-300 shadow-md">
            {{ translate('help.about.failedToLoad') }}{{ error }}
        </div>

        <div v-else class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_18rem]">
            <div class="space-y-6">
                <section
                    v-if="overviewHtml"
                    id="about-overview"
                    class="bg-primary/35 border-accent/15 rounded-2xl border p-6 shadow-md">
                    <div class="mb-4 flex items-center justify-between gap-3">
                        <h2 class="text-xl font-semibold text-primary-content">
                            Overview
                        </h2>
                        <span
                            class="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs font-semibold tracking-[0.16em] uppercase text-primary-content/55">
                            Documentation
                        </span>
                    </div>
                    <div class="about-markdown" v-html="overviewHtml"></div>
                </section>

                <section
                    v-for="section in readmeSections"
                    :key="section.id"
                    :id="section.id"
                    class="bg-primary/35 border-accent/15 rounded-2xl border p-6 shadow-md">
                    <div class="mb-4 flex items-center justify-between gap-3">
                        <h2 class="text-xl font-semibold text-primary-content">
                            {{ section.title }}
                        </h2>
                        <a
                            :href="`#${section.id}`"
                            class="text-primary-content/45 hover:text-accent text-sm transition-colors">
                            #
                        </a>
                    </div>
                    <div class="about-markdown" v-html="section.html"></div>
                </section>
            </div>

            <aside v-if="tocItems.length > 0" class="hidden xl:block">
                <div
                    class="bg-primary/35 border-accent/15 sticky top-4 rounded-2xl border p-5 shadow-md">
                    <h2 class="text-sm font-semibold tracking-[0.18em] uppercase text-primary-content/60">
                        On This Page
                    </h2>
                    <div class="mt-4 space-y-2">
                        <a
                            v-for="item in tocItems"
                            :key="item.id"
                            :href="`#${item.id}`"
                            class="text-primary-content/65 hover:text-primary-content hover:bg-primary/45 block rounded-lg px-3 py-2 text-sm transition-colors">
                            {{ item.title }}
                        </a>
                    </div>
                </div>
            </aside>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { marked } from 'marked'
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
        return 'border-green-500/30 bg-green-500/15 text-green-300'
    }

    if (version.value.isDevBuild) {
        return 'border-sky-500/30 bg-sky-500/15 text-sky-300'
    }

    return 'border-white/12 bg-white/6 text-primary-content/72'
})

const tocItems = computed(() => {
    const items: Array<{ id: string; title: string }> = []

    if (overviewHtml.value) {
        items.push({
            id: 'about-overview',
            title: 'Overview'
        })
    }

    readmeSections.value.forEach((section) => {
        items.push({
            id: section.id,
            title: section.title
        })
    })

    return items
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

.about-markdown :deep(h3) {
    margin-top: 1.75rem;
    margin-bottom: 0.75rem;
    color: var(--primary-content);
    font-size: 1.05rem;
    font-weight: 700;
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
    border-left: 3px solid color-mix(in srgb, var(--accent) 55%, transparent);
    background: color-mix(in srgb, var(--primary) 38%, transparent);
    border-radius: 0.9rem;
    padding: 1rem 1.1rem;
    color: var(--primary-content);
}

.about-markdown :deep(code) {
    border-radius: 0.45rem;
    background: color-mix(in srgb, var(--primary) 55%, transparent);
    padding: 0.15rem 0.35rem;
    color: var(--primary-content);
    font-size: 0.92em;
}

.about-markdown :deep(pre) {
    overflow-x: auto;
    border: 1px solid color-mix(in srgb, var(--accent) 18%, transparent);
    border-radius: 1rem;
    background: color-mix(in srgb, var(--primary) 70%, transparent);
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
    border: 1px solid color-mix(in srgb, var(--accent) 14%, transparent);
    border-radius: 1rem;
}

.about-markdown :deep(th),
.about-markdown :deep(td) {
    min-width: 12rem;
    border-bottom: 1px solid color-mix(in srgb, var(--accent) 12%, transparent);
    padding: 0.85rem 1rem;
    text-align: left;
    vertical-align: top;
}

.about-markdown :deep(th) {
    background: color-mix(in srgb, var(--primary) 55%, transparent);
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
