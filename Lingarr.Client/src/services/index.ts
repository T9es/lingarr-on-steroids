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
import { directoryService } from '@/services/directoryService'
import { statisticsService } from '@/services/statisticsService'
import { logsService } from '@/services/logsService'
import { chutesService } from '@/services/chutesService'

const services = (axios: AxiosStatic): Services => ({
    setting: settingService(axios),
    subtitle: subtitleService(axios),
    translate: translateService(axios),
    chutes: chutesService(axios),
    translationRequest: translationRequestService(axios),
    version: versionService(axios),
    media: mediaService(axios),
    schedule: scheduleService(axios),
    mapping: mappingService(axios),
    directory: directoryService(axios),
    statistics: statisticsService(axios),
    logs: logsService()
})

export default services(axios)
