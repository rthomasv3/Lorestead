const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

function pad(value) {
  return String(value).padStart(2, '0')
}

// Formats an ISO timestamp using the application settings presets (SettingsView
// offers a fixed list, so this maps them exactly rather than parsing tokens).
export function formatTimestamp(iso, dateFormat, timeFormat) {
  const date = new Date(iso)
  if (!iso || isNaN(date.getTime())) return ''

  const year = date.getFullYear()
  const month = date.getMonth() + 1
  const day = date.getDate()
  let datePart
  switch (dateFormat) {
    case 'MM/dd/yyyy':
      datePart = `${pad(month)}/${pad(day)}/${year}`
      break
    case 'dd/MM/yyyy':
      datePart = `${pad(day)}/${pad(month)}/${year}`
      break
    case 'MMM d, yyyy':
      datePart = `${MONTHS[month - 1]} ${day}, ${year}`
      break
    default:
      datePart = `${year}-${pad(month)}-${pad(day)}`
  }

  const hours = date.getHours()
  const minutes = pad(date.getMinutes())
  let timePart
  if (timeFormat === 'h:mm tt') {
    timePart = `${hours % 12 || 12}:${minutes} ${hours >= 12 ? 'PM' : 'AM'}`
  } else {
    timePart = `${pad(hours)}:${minutes}`
  }

  return `${datePart} ${timePart}`
}
