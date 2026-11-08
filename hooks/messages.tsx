import type { On } from 'claude-code'

import { withOpacity } from './settings'
import { readSettings } from './state'

// A soft background for the person's bubble when only its text colour is chosen.
const NEUTRAL_BUBBLE = 'rgba(127, 127, 127, 0.18)'

/**
 * Message colours, drawn by the app itself (so they scroll with the chat and keep the text
 * sharp): the person's own messages in a bubble of their colours, and Claude's replies on a
 * background. Desktop only; with no colour chosen, the app's own drawing is left as it is.
 */
export function registerMessageColors(on: On): void {
  on('ui.render', { component: 'UserMessage' }, async ($, e, next) => {
    // Only the person's own prompts, drawn in full; not task notices or other agents' messages.
    if (e.surface !== 'desktop' || e.props.task || e.props.from || !e.props.isExpanded) {
      return next(e)
    }

    const settings = await readSettings($)
    if (!settings || (!settings.userBubbleColor && !settings.userTextColor)) {
      return next(e)
    }

    // The app's drawing cannot take a text colour, so the bubble is drawn here instead. The
    // outer row keeps it on the right, as the app places the person's messages.
    const { Box, Text } = $.ui.resolve(e)
    const background = settings.userBubbleColor ?? NEUTRAL_BUBBLE
    return (
      <Box flexDirection="row" justifyContent="flex-end" width="100%">
        <Box backgroundColor={background} borderStyle="round" borderColor={background} paddingX={2} paddingY={1}>
          <Text color={settings.userTextColor ?? undefined}>{e.props.text}</Text>
        </Box>
      </Box>
    )
  })

  on('ui.render', { component: 'AssistantMessage' }, async ($, e, next) => {
    if (e.surface !== 'desktop') {
      return next(e)
    }

    const settings = await readSettings($)
    if (!settings?.replyBackgroundColor) {
      return next(e)
    }

    // Around the app's own drawing of the reply, so code blocks, links and pictures stay.
    const { Box } = $.ui.resolve(e)
    const background = withOpacity(settings.replyBackgroundColor, settings.replyBackgroundOpacity)
    const reply = await next(e)
    return (
      <Box backgroundColor={background} borderStyle="round" borderColor={background} paddingX={2} paddingY={1}>
        {reply}
      </Box>
    )
  })
}
