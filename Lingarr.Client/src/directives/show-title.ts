export default {
    mounted(el: HTMLElement) {
        const text = el.textContent
        const originalClasses = el.className
        if (!text) return

        const parts = text.split(' - ')

        if (parts.length < 3) {
            el.className = `${originalClasses} block max-w-full min-w-0 truncate`
            return
        }

        const [showName, episodeNumber, ...episodeTitleParts] = parts
        const episodeTitle = episodeTitleParts.join(' - ')

        const wrapper = document.createElement('div')
        wrapper.className = `${originalClasses} flex max-w-full min-w-0 items-center gap-1.5 overflow-hidden whitespace-nowrap`

        const showNameSpan = document.createElement('span')
        showNameSpan.className = 'min-w-0 shrink truncate'
        showNameSpan.textContent = showName

        const episodeNumberSpan = document.createElement('span')
        episodeNumberSpan.className = 'shrink-0'
        episodeNumberSpan.textContent = episodeNumber

        const episodeTitleSpan = document.createElement('span')
        episodeTitleSpan.className = 'text-primary-content/50 block min-w-0 flex-1 truncate'
        episodeTitleSpan.textContent = episodeTitle

        for (const child of [
            showNameSpan,
            document.createTextNode('-'),
            episodeNumberSpan,
            document.createTextNode('-'),
            episodeTitleSpan
        ]) {
            wrapper.append(child)
        }

        el.replaceChildren(wrapper)
    }
}
