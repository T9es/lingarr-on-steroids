<template>
    <div class="space-y-8">
        <!-- Radarr Section -->
        <div>
            <h3 class="text-primary-content mb-3 text-lg font-semibold">Radarr</h3>
            <div class="flex flex-wrap gap-4">
                <InstanceCard
                    v-for="instance in onboardingStore.radarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="radarr"
                    class="w-full md:w-[calc(50%-0.5rem)] xl:w-[calc(33.333%-0.667rem)]"
                    :connection-status="getConnectionStatus('radarr', instance.id)"
                    @update:instance="updateRadarrInstance(instance.id, $event)"
                    @remove="removeRadarrInstance(instance.id)"
                    @test-connection="testRadarrConnection(instance)" />
                
                <AddInstanceButton
                    v-if="onboardingStore.radarrInstances.length < 5"
                    type="radarr"
                    class="w-full md:w-[calc(50%-0.5rem)] xl:w-[calc(33.333%-0.667rem)]"
                    @add="addRadarrInstance" />
            </div>
        </div>

        <!-- Sonarr Section -->
        <div>
            <h3 class="text-primary-content mb-3 text-lg font-semibold">Sonarr</h3>
            <div class="flex flex-wrap gap-4">
                <InstanceCard
                    v-for="instance in onboardingStore.sonarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="sonarr"
                    class="w-full md:w-[calc(50%-0.5rem)] xl