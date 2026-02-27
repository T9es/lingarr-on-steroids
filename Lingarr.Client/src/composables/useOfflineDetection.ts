import { ref, onMounted, onUnmounted } from 'vue'

export function useOfflineDetection() {
    const isOnline = ref(navigator.onLine)

    const handleOnline = () => {
        isOnline.value = true
    }

    const handleOffline = () => {
        isOnline.value = false
    }

    onMounted(() => {
        window.addEventListener('online', handleOnline)
        window.addEventListener('offline', handleOffline)
    })

    onUnmounted(() => {
        window.removeEventListener('online', handleOnline)
        window.removeEventListener('offline', handleOffline)
    })

    return {
        isOnline
    }
}
