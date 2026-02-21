<template>
  <div class="p-6 space-y-6">
    <CardComponent title="About Lingarr">
      <template #description>
        Lingarr on Steroids is a specialized fork of Lingarr re-engineered for enhanced reliability,
        performance, and cost-effective AI usage in subtitle translation workflows.
      </template>
      <template #content>
        <!-- Version Info -->
        <div class="mb-4">
          <span class="text-secondary-content">Version:</span>
          <span class="font-semibold ml-2">{{ currentVersion }}</span>
        </div>

        <!-- GitHub Link -->
        <a
          href="https://github.com/T9es/lingarr-on-steroids"
          target="_blank"
          rel="noopener noreferrer"
          class="flex items-center gap-2 text-accent hover:brightness-125 mb-4"
        >
          <GithubIcon class="h-4 w-4" />
          <span>View on GitHub</span>
        </a>

        <!-- README Content -->
        <div v-if="loading" class="flex items-center gap-2">
          <LoaderCircleIcon class="h-5 w-5 animate-spin" />
          <span>Loading documentation...</span>
        </div>
        <div v-else-if="error" class="text-red-500">
          Failed to load documentation: {{ error }}
        </div>
        <div v-else class="prose prose-invert max-w-none" v-html="readmeContent"></div>
      </template>
    </CardComponent>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import GithubIcon from '@/components/icons/GithubIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useInstanceStore } from '@/store/instance'

const instanceStore = useInstanceStore()
const currentVersion = computed(() => instanceStore.version.currentVersion || 'Unknown')

const loading = ref(true)
const error = ref<string | null>(null)
const readmeContent = ref('')

/**
 * Simple markdown to HTML converter for basic formatting
 * Uses inline Tailwind classes for styling
 */
function simpleMarkdownToHtml(markdown: string): string {
  let html = markdown

  // Escape HTML entities first
  html = html
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')

  // Headers
  html = html.replace(/^### (.*$)/gm, '<h3 class="text-lg font-bold mt-4 mb-2 text-primary-content">$1</h3>')
  html = html.replace(/^## (.*$)/gm, '<h2 class="text-xl font-bold mt-6 mb-3 text-primary-content">$1</h2>')
  html = html.replace(/^# (.*$)/gm, '<h1 class="text-2xl font-bold mt-6 mb-4 text-primary-content">$1</h1>')

  // Bold
  html = html.replace(/\*\*(.*?)\*\*/g, '<strong class="font-bold">$1</strong>')
  html = html.replace(/__(.*?)__/g, '<strong class="font-bold">$1</strong>')

  // Italic
  html = html.replace(/\*(.*?)\*/g, '<em>$1</em>')
  html = html.replace(/_(.*?)_/g, '<em>$1</em>')

  // Code blocks
  html = html.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre class="bg-secondary/50 p-4 rounded-md overflow-x-auto my-4 text-sm"><code>$2</code></pre>')

  // Inline code
  html = html.replace(/`([^`]+)`/g, '<code class="bg-secondary/50 px-1.5 py-0.5 rounded text-sm">$1</code>')

  // Links
  html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" class="text-accent hover:brightness-125 underline" target="_blank" rel="noopener noreferrer">$1</a>')

  // Unordered lists
  html = html.replace(/^- (.*$)/gm, '<li class="ml-4 text-secondary-content">$1</li>')
  html = html.replace(/(<li.*<\/li>\n?)+/g, '<ul class="list-disc my-2">$&</ul>')

  // Horizontal rules
  html = html.replace(/^---$/gm, '<hr class="border-secondary my-4" />')

  // Tables
  html = html.replace(/^\|(.+)\|$/gm, (match, content) => {
    const cells = content.split('|').map((cell: string) => cell.trim())
    if (cells.every((cell: string) => cell.match(/^-+$/))) {
      return '<!-- table separator -->'
    }
    return match
  })

  // Paragraphs (lines not already wrapped in tags)
  html = html.split('\n\n').map(block => {
    if (block.trim() && !block.match(/^<(h[1-6]|ul|ol|li|pre|hr|blockquote|div|!--)/)) {
      return `<p class="my-2 text-secondary-content">${block}</p>`
    }
    return block
  }).join('\n')

  // Line breaks
  html = html.replace(/\n/g, '<br />')

  return html
}

async function fetchReadme() {
  try {
    loading.value = true
    error.value = null

    const response = await fetch('https://api.github.com/repos/T9es/lingarr-on-steroids/readme', {
      headers: {
        Accept: 'application/vnd.github.v3+json'
      }
    })

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`)
    }

    const data = await response.json()
    
    // Decode base64 content
    const decodedContent = atob(data.content)
    readmeContent.value = simpleMarkdownToHtml(decodedContent)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error occurred'
    console.error('Failed to fetch README:', err)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchReadme()
})
</script>
