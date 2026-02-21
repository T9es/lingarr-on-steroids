<template>
  <div class="p-6 space-y-6">
    <CardComponent title="Onboarding Assistant">
      <template #description>
        The onboarding wizard helps you configure Lingarr for the first time.
        It will guide you through connecting Radarr/Sonarr, selecting a translation service,
        and choosing your languages.
      </template>
      <template #content>
        <div v-if="onboardingCompleted" class="text-green-500 mb-4">
          Onboarding was completed previously.
        </div>
        <div v-if="onboardingSkipped" class="text-yellow-500 mb-4">
          Onboarding was skipped previously.
        </div>
        <button
          class="bg-accent hover:brightness-125 rounded-md px-4 py-2 text-white transition-all"
          @click="startOnboarding"
        >
          Start Onboarding Wizard
        </button>
      </template>
    </CardComponent>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import { useOnboardingStore } from '@/store/onboarding'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts/setting'

const onboardingStore = useOnboardingStore()
const settingStore = useSettingStore()

const onboardingCompleted = computed(() => {
  return settingStore.getSetting(SETTINGS.ONBOARDING_COMPLETED) === 'true'
})

const onboardingSkipped = computed(() => {
  return settingStore.getSetting(SETTINGS.ONBOARDING_SKIPPED) === 'true'
})

const startOnboarding = () => {
  onboardingStore.start()
}

onMounted(() => {
  settingStore.applySettingsOnLoad()
})
</script>
