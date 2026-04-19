import axios, { AxiosStatic } from 'axios'
import { Services } from '@/ts'
import { subtitleService } from './subtitleService'
import { translateService } from './translateService'
import { settingService } from './settingService'
import { mediaService } from './mediaService'
import { versionService } from './versionService'
import { scheduleService } from '@/services/scheduleService'
import { translationRequestService } from '@/services/translationRequestService'
import { mappingService } from '@/services/mappingService'
import { customSourceService } from '@/services/customSourceService'
import { directoryService } from '@/services/directoryService'
import { uploadWorkspaceService } from '@/services/uploadWorkspaceService'
import { statisticsService } from '@/services/statisticsService'
import { logsService } from '@/services/logsService'
import { chutesService } from '@/services/chutesService'
import { tokenUsageService } from '@/services/tokenUsageService'
import { dashboardService } from '@/services/dashboardService'

const services = (axios: AxiosStatic): Services => ({
    setting: settingService(axios),
    subtitle: subtitleService(axios),
    translate: translateService(axios),
    chutes: chutesService(axios),
    tokenUsage: tokenUsageService(axios),
    translationRequest: translationRequestService(axios),
    version: versionService(axios),
    media: mediaService(axios),
    schedule: scheduleService(axios),
    mapping: mappingService(axios),
    customSources: customSourceService(axios),
    directory: directoryService(axios),
    uploadWorkspace: uploadWorkspaceService(axios),
    statistics: statisticsService(axios),
    logs: logsService(),
    dashboard: dashboardService(axios)
})

export default services(import.meta.env.DEV ? axios : axios)
