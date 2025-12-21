<template>
    <CardComponent :title="translate('settings.subtitle.title')">
        <template #description>
            {{ translate('settings.subtitle.description') }}
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <SaveNotification ref="saveNotification" />
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.ignoreCaptions') }}
                    </span>
                    {{ translate('settings.subtitle.ignoreCaptionsDescription') }}
                </div>
                <ToggleButton v-model="ignoreCaptions">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            ignoreCaptions == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.fixOverlappingSubtitles') }}
                    </span>
                    {{ translate('settings.subtitle.fixOverlappingSubtitlesDescription') }}
                </div>
                <ToggleButton v-model="fixOverlappingSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            fixOverlappingSubtitles == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.stripSubtitleFormatting') }}
                    </span>
                    {{ translate('settings.subtitle.stripSubtitleFormattingDescription') }}
                </div>
                <ToggleButton v-model="stripSubtitleFormatting">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            stripSubtitleFormatting == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.addTranslatorInfo') }}
                    </span>
                    {{ translate('settings.subtitle.addTranslatorInfoDescription') }}
                </div>
                <ToggleButton v-model="addTranslatorInfo">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            addTranslatorInfo == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.stripAssDrawingCommands') }}
                    </span>
                    {{ translate('settings.subtitle.stripAssDrawingCommandsDescription') }}
                </div>
                <ToggleButton v-model="stripAssDrawingCommands">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            stripAssDrawingCommands == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div v-if="stripAssDrawingCommands == 'true'" class="ml-4 flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.cleanSourceAssDrawings') }}
                    </span>
                    {{ translate('settings.subtitle.cleanSourceAssDrawingsDescription') }}
                </div>
                <ToggleButton
                    v-if="stripAssDrawingCommands == 'true'"
                    v-model="cleanSourceAssDrawings">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            cleanSourceAssDrawings == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.removeLanguageTag') }}
                    </span>
                    {{ translate('settings.subtitle.removeLanguageTagDescription') }}
                </div>
                <ToggleButton v-model="removeLanguageTag">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            removeLanguageTag == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-y-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.useSubtitleTagging') }}
                        </span>
                        {{ translate('settings.subtitle.useSubtitleTaggingDescription') }}
                    </div>
                    <ToggleButton v-model="useSubtitleTagging">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                useSubtitleTagging == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTag"
                        validation-type="string"
                        :label="translate('settings.subtitle.subtitleTag')"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTagShort"
                        validation-type="string"
                        :label="translate('settings.subtitle.subtitleTagShort')"
                        :description="translate('settings.subtitle.subtitleTagShortDescription')"
                        @update:validation="(val) => (isValid.subtitleTagShort = val)" />
                </div>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'

import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isValid = reactive({
    subtitleTag: true,
    subtitleTagShort: true
})

const ignoreCaptions = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.IGNORE_CAPTIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.IGNORE_CAPTIONS, newValue, true)
        saveNotification.value?.show()
    }
})

const fixOverlappingSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const stripSubtitleFormatting = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING, newValue, true)
        saveNotification.value?.show()
    }
})

const addTranslatorInfo = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ADD_TRANSLATOR_INFO) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ADD_TRANSLATOR_INFO, newValue, true)
        saveNotification.value?.show()
    }
})

const removeLanguageTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.REMOVE_LANGUAGE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.REMOVE_LANGUAGE_TAG, newValue, true)
        saveNotification.value?.show()
    }
})

const useSubtitleTagging = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
        saveNotification.value?.show()
    }
})

const subtitleTagShort = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG_SHORT) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG_SHORT, newValue, isValid.subtitleTagShort)
        saveNotification.value?.show()
    }
})

const stripAssDrawingCommands = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS, newValue, true)
        // If disabling, also disable the dependent setting
        if (newValue === 'false') {
            settingsStore.updateSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS, 'false', true)
        }
        saveNotification.value?.show()
    }
})

const cleanSourceAssDrawings = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS, newValue, true)
        saveNotification.value?.show()
    }
})
</script>
