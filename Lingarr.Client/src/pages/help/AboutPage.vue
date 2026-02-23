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
        <div v-else class="prose prose-invert max-w-none 
          prose-headings:text-primary-content 
          prose-p:text-secondary-content 
          prose-a:text-accent 
          prose-strong:text-primary-content 
          prose-code:text-primary-content 
          prose-pre:bg-primary 
          prose-th:border-accent 
          prose-th:bg-primary/50 
          prose-th:text-primary-content 
          prose-td:border-accent/50 
          prose-td:text-secondary-content
          prose-li:text-secondary-content
          prose-ul:text-secondary-content
          prose-ol:text-secondary-content" 
          v-html="readmeContent">
        </div>
      </template>
    </CardComponent>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { marked } from 'marked'
import CardComponent from '@/components/common/CardComponent.vue'
import GithubIcon from '@/components/icons/GithubIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useInstanceStore } from '@/store/instance'

// Configure marked for GitHub Flavored Markdown
marked.setOptions({
  gfm: true,
  breaks: true
})

const instanceStore = useInstanceStore()
const currentVersion = computed(() => instanceStore.version.currentVersion || 'Unknown')

const loading = ref(true)
const error = ref<string | null>(null)
const readmeContent = ref('')

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
    
    // Decode base64 content with proper UTF-8 handling
    // atob() only handles ASCII - use TextDecoder for UTF-8 characters like •, —, etc.
    const binaryString = atob(data.content)
    const bytes = new Uint8Array(binaryString.length)
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i)
    }
    const decodedContent = new TextDecoder('utf-8').decode(bytes)
    readmeContent.value = await marked.parse(decodedContent) as string
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
